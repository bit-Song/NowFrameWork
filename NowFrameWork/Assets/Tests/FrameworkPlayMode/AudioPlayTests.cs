using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Audio;
using NowFrameWork.Core;
using NowFrameWork.Core.Platform;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace NowFrameWork.Tests
{
    public sealed class AudioPlayTests
    {
        sealed class Provider : IAssetProvider, IDisposable
        {
            readonly List<AudioClip> _clips = new List<AudioClip>();
            public int Loads;
            public bool FailRelease;
            public UniTaskCompletionSource<ProviderHandle> Pending;

            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
                => UniTask.FromResult(key.ToString());

            public UniTask<ProviderHandle> LoadAsync(string key, CancellationToken ct)
            {
                Loads++;
                return key == "pending" ? Pending.Task : UniTask.FromResult(CreateHandle(key));
            }

            public ProviderHandle CreateHandle(string key)
            {
                int samples = key == "short" ? 8820 : 44100;
                var clip = AudioClip.Create(key, samples, 1, 44100, false);
                _clips.Add(clip);
                return new ProviderHandle(key, clip);
            }

            public void Release(ProviderHandle handle)
            {
                if (FailRelease) throw new InvalidOperationException("Release failed.");
            }

            public void Dispose()
            {
                foreach (var clip in _clips) Object.Destroy(clip);
            }
        }

        [UnityTest]
        public IEnumerator EffectsShareLeaseAndReuseSources() => UniTask.ToCoroutine(async () =>
        {
            using var provider = new Provider();
            var assets = new AssetService(provider);
            var lifecycle = new AppLifecycle();
            var host = new GameObject("Audio test");
            var module = new AudioModule(assets, lifecycle, host.transform, maxEffects: 2);
            await module.InitializeAsync(default);
            await module.StartAsync(default);
            try
            {
                module.SetVolumes(0.25f, 0.5f);
                lifecycle.NotifyPause();
                Assert.That(await module.PlayEffectAsync("effect"), Is.True);
                Assert.That(await module.PlayEffectAsync("effect"), Is.True);
                Assert.That(await module.PlayEffectAsync("effect"), Is.False);
                Assert.That(assets.ActiveLeases, Is.EqualTo(1));
                Assert.That(provider.Loads, Is.EqualTo(1));
                module.Tick(0f, 0f);
                Assert.That(assets.ActiveLeases, Is.EqualTo(1));
                var sources = host.GetComponentsInChildren<AudioSource>();
                Assert.That(sources, Has.Length.EqualTo(3));
                Assert.That(sources[1].volume, Is.EqualTo(0.5f));
                Assert.That(sources[1].clip, Is.SameAs(sources[2].clip));
                module.SetPaused(true);
                lifecycle.NotifyResume();
                Assert.That(module.IsPaused, Is.True);
                module.SetPaused(false);
                Assert.That(module.IsPaused, Is.False);
                module.StopEffects();
                Assert.That(assets.ActiveLeases, Is.Zero);
                Assert.That(sources[1].clip, Is.Null);
                lifecycle.NotifyPause();
                Assert.That(await module.PlayEffectAsync("effect"), Is.True);
                Assert.That(host.GetComponentsInChildren<AudioSource>(), Has.Length.EqualTo(3));
            }
            finally
            {
                await module.DisposeAsync();
                Object.Destroy(host);
            }
            Assert.That(assets.ActiveLeases, Is.Zero);
        });

        [UnityTest]
        public IEnumerator MusicSwitchUsesLatestRequestAndShutdownCanRetry() => UniTask.ToCoroutine(async () =>
        {
            using var provider = new Provider { Pending = new UniTaskCompletionSource<ProviderHandle>() };
            var assets = new AssetService(provider);
            var lifecycle = new AppLifecycle();
            lifecycle.NotifyPause();
            var host = new GameObject("Audio test");
            var module = new AudioModule(assets, lifecycle, host.transform);
            await module.InitializeAsync(default);
            await module.StartAsync(default);
            try
            {
                Assert.That(await module.PlayMusicAsync("original"), Is.True);
                var source = host.GetComponentInChildren<AudioSource>();
                Assert.That(source.loop, Is.True);
                var slow = module.PlayMusicAsync("pending");
                Assert.That(source.clip.name, Is.EqualTo("original"));
                Assert.That(await module.PlayMusicAsync("latest"), Is.True);
                provider.Pending.TrySetResult(provider.CreateHandle("pending"));
                Assert.That(await slow, Is.False);
                Assert.That(source.clip.name, Is.EqualTo("latest"));
                Assert.That(assets.ActiveLeases, Is.EqualTo(1));

                provider.FailRelease = true;
                Exception failure = null;
                try { await module.DisposeAsync(); }
                catch (AggregateException error) { failure = error; }
                Assert.That(failure, Is.Not.Null);
                Assert.That(source.clip, Is.Null);
                Assert.That(assets.ActiveLeases, Is.EqualTo(1));
            }
            finally
            {
                provider.FailRelease = false;
                await module.DisposeAsync();
                Object.Destroy(host);
            }
            Assert.That(assets.ActiveLeases, Is.Zero);
        });

        [UnityTest]
        public IEnumerator CompletedEffectReturnsLeaseOnTick() => UniTask.ToCoroutine(async () =>
        {
            using var provider = new Provider();
            var assets = new AssetService(provider);
            var host = new GameObject("Audio test");
            var module = new AudioModule(assets, new AppLifecycle(), host.transform);
            await module.InitializeAsync(default);
            await module.StartAsync(default);
            try
            {
                Assert.That(await module.PlayEffectAsync("short"), Is.True);
                var source = Array.Find(host.GetComponentsInChildren<AudioSource>(), item => item.clip != null);
                Assert.That(source.isPlaying, Is.True);
                using var timeout = new CancellationTokenSource();
                using var timer = timeout.CancelAfterSlim(TimeSpan.FromSeconds(5));
                await UniTask.WaitUntil(() => !source.isPlaying, cancellationToken: timeout.Token);
                module.Tick(0f, 0f);
                Assert.That(source.clip, Is.Null);
                Assert.That(assets.ActiveLeases, Is.Zero);
            }
            finally
            {
                await module.DisposeAsync();
                Object.Destroy(host);
            }
        });

        [UnityTest]
        public IEnumerator StopCancelsPendingPlaybackAndAllowsLateLoadCleanup() => UniTask.ToCoroutine(async () =>
        {
            using var provider = new Provider { Pending = new UniTaskCompletionSource<ProviderHandle>() };
            var assets = new AssetService(provider);
            var lifecycle = new AppLifecycle();
            var host = new GameObject("Audio test");
            var module = new AudioModule(assets, lifecycle, host.transform);
            var hub = new ModuleHub();
            hub.Register(module);
            await hub.StartupAsync();
            try
            {
                var effect = module.PlayEffectAsync("pending");
                await module.DisposeAsync();
                bool canceled = false;
                try { await effect; }
                catch (OperationCanceledException) { canceled = true; }
                Assert.That(canceled, Is.True);

                provider.Pending.TrySetResult(provider.CreateHandle("pending"));
                Assert.That(assets.PendingRequests, Is.Zero);
                Assert.That(assets.ActiveLeases, Is.Zero);
            }
            finally
            {
                await hub.ShutdownAsync();
                Object.Destroy(host);
            }
        });
    }
}
