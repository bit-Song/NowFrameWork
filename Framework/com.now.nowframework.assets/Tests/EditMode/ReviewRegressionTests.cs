using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core;
using NowFrameWork.Core.Time;
using UnityEngine;

namespace NowFrameWork.Assets.Tests
{
    public class ReviewRegressionTests
    {
        sealed class TestAsset : ScriptableObject { }
        readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        TestAsset NewAsset() { var asset = ScriptableObject.CreateInstance<TestAsset>(); _objects.Add(asset); return asset; }
        [TearDown] public void Cleanup() { foreach (var asset in _objects) if (asset != null) UnityEngine.Object.DestroyImmediate(asset); _objects.Clear(); }

        sealed class Provider : IAssetProvider, IAssetProviderLifecycle
        {
            public readonly UniTaskCompletionSource<ProviderHandle> Gate = new UniTaskCompletionSource<ProviderHandle>();
            public int Releases;
            public int Initializes;
            public UniTask InitializeAsync(CancellationToken ct) { Initializes++; return UniTask.CompletedTask; }
            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct) => UniTask.FromResult("canonical");
            public UniTask<ProviderHandle> LoadAsync(string key, CancellationToken ct) => Gate.Task;
            public void Release(ProviderHandle handle) => Releases++;
        }
        sealed class Module : IAppModule
        {
            public readonly UniTaskCompletionSource Gate = new UniTaskCompletionSource();
            public string Id => "module";
            public IReadOnlyList<string> DependsOn => Array.Empty<string>();
            public UniTask InitializeAsync(CancellationToken ct) => Gate.Task;
            public UniTask StartAsync(CancellationToken ct) => UniTask.CompletedTask;
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }
        sealed class TypedProvider : IAssetProvider, ITypedAssetProvider
        {
            public Type RequestedType;
            public string SubObject;
            public UnityEngine.Object Asset;
            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
                => throw new InvalidOperationException("应使用类型化定位");
            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, Type type, CancellationToken ct)
            {
                RequestedType = type;
                SubObject = key.SubObject;
                return UniTask.FromResult(key.Address + "[" + key.SubObject + "]");
            }
            public UniTask<ProviderHandle> LoadAsync(string key, CancellationToken ct)
                => UniTask.FromResult(new ProviderHandle(key, Asset));
            public void Release(ProviderHandle handle) { }
        }
        static async UniTask AcquireAndImmediatelyRelease(AssetService service)
        {
            var lease = await service.AcquireAsync<TestAsset>("a", default);
            lease.Dispose();
        }

        [Test]
        public void FirstWaiter_ReleasesImmediately_SecondStillReceivesLease()
        {
            var provider = new Provider();
            var service = new AssetService(provider);
            var first = AcquireAndImmediatelyRelease(service).AsTask();
            var second = service.AcquireAsync<TestAsset>("a", default).AsTask();
            provider.Gate.TrySetResult(new ProviderHandle("canonical", NewAsset()));
            first.GetAwaiter().GetResult();
            var lease = second.GetAwaiter().GetResult();
            Assert.IsNotNull(lease.Asset);
            Assert.AreEqual(0, provider.Releases);
            lease.Dispose();
            Assert.AreEqual(1, provider.Releases);
            Assert.AreEqual(0, service.ActiveLeases);
        }

        [Test]
        public void Config_ConcurrentSameKey_KeepsOneLease()
        {
            var provider = new Provider();
            var service = new AssetService(provider);
            var config = new ConfigService(service);
            var a = config.LoadAsync<TestAsset>("a").AsTask();
            var b = config.LoadAsync<TestAsset>("a").AsTask();
            provider.Gate.TrySetResult(new ProviderHandle("canonical", NewAsset()));
            Assert.AreSame(a.GetAwaiter().GetResult(), b.GetAwaiter().GetResult());
            Assert.AreEqual(1, service.ActiveLeases);
            config.DisposeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, service.ActiveLeases);
            Assert.AreEqual(1, provider.Releases);
        }

        [Test]
        public void Config_DisposedDuringLoad_ReleasesLateLease()
        {
            var provider = new Provider();
            var service = new AssetService(provider);
            var config = new ConfigService(service);
            var pending = config.LoadAsync<TestAsset>("a").AsTask();
            config.DisposeAsync().GetAwaiter().GetResult();
            provider.Gate.TrySetResult(new ProviderHandle("canonical", NewAsset()));
            Assert.Throws<ObjectDisposedException>(() => pending.GetAwaiter().GetResult());
            Assert.AreEqual(0, config.Count);
            Assert.AreEqual(0, service.ActiveLeases);
            Assert.AreEqual(1, provider.Releases);
        }

        [Test]
        public void Config_AliasesAndVariants_ShareCanonicalLease()
        {
            var provider = new Provider();
            provider.Gate.TrySetResult(new ProviderHandle("canonical", NewAsset()));
            var service = new AssetService(provider);
            var config = new ConfigService(service);
            config.LoadAsync<TestAsset>(new AssetKey("a", variant: "one")).GetAwaiter().GetResult();
            config.LoadAsync<TestAsset>(new AssetKey("a", variant: "two")).GetAwaiter().GetResult();
            config.LoadAsync<TestAsset>("guid-alias").GetAwaiter().GetResult();
            Assert.AreEqual(1, service.ActiveLeases);
            config.DisposeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, provider.Releases);
        }

        [Test]
        public void AssetsModule_InitializesProvider()
        {
            var provider = new Provider();
            var module = new AssetsModule(new AssetService(provider));
            module.InitializeAsync(default).GetAwaiter().GetResult();
            Assert.AreEqual(1, provider.Initializes);
        }

        [Test]
        public void SubObjectResolution_ForwardsRequestedTypeAndName()
        {
            var provider = new TypedProvider { Asset = NewAsset() };
            var service = new AssetService(provider);
            var lease = service.AcquireAsync<TestAsset>(new AssetKey("atlas", "icon"), default).GetAwaiter().GetResult();
            Assert.AreEqual(typeof(TestAsset), provider.RequestedType);
            Assert.AreEqual("icon", provider.SubObject);
            Assert.AreEqual("atlas[icon]", lease.CanonicalKey);
            lease.Dispose();
        }

        [Test]
        public void Scope_ClosingParent_WaitsForStartingChild()
        {
            var root = new Scope("root", null, new ManualClock(), frameWaiter: () => UniTask.CompletedTask);
            root.StartupAsync().GetAwaiter().GetResult();
            var child = root.CreateChild("child");
            var module = new Module();
            child.Modules.Register(module);
            var startup = child.StartupAsync().AsTask();
            var closing = root.DisposeAsync().AsTask();
            Assert.IsFalse(closing.IsCompleted);
            Assert.AreEqual(ScopeState.Stopping, root.State);
            module.Gate.TrySetResult();
            startup.GetAwaiter().GetResult();
            closing.GetAwaiter().GetResult();
            Assert.AreEqual(ScopeState.Stopped, child.State);
            Assert.AreEqual(ScopeState.Stopped, root.State);
        }

        [Test]
        public void Scope_ConcurrentClose_WaitsForSameCleanup()
        {
            var gate = new UniTaskCompletionSource();
            var scope = new Scope("root", null, new ManualClock(), frameWaiter: () => gate.Task);
            scope.StartupAsync().GetAwaiter().GetResult();
            var a = scope.DisposeAsync().AsTask();
            var b = scope.DisposeAsync().AsTask();
            Assert.IsFalse(a.IsCompleted);
            Assert.IsFalse(b.IsCompleted);
            gate.TrySetResult();
            a.GetAwaiter().GetResult();
            b.GetAwaiter().GetResult();
            Assert.AreEqual(ScopeState.Stopped, scope.State);
        }

        [Test]
        public void SceneLease_ConcurrentUnload_CallsBackendOnce()
        {
            var gate = new UniTaskCompletionSource();
            int calls = 0;
            var lease = new SceneLease("scene", () => { calls++; return gate.Task; });
            var a = lease.UnloadAsync().AsTask();
            var b = lease.UnloadAsync().AsTask();
            Assert.AreEqual(1, calls);
            Assert.IsFalse(b.IsCompleted);
            gate.TrySetResult();
            a.GetAwaiter().GetResult();
            b.GetAwaiter().GetResult();
            Assert.IsTrue(lease.IsUnloaded);
        }

        [Test]
        public void SceneLease_Failure_CanRetry()
        {
            int calls = 0;
            var lease = new SceneLease("scene", () => ++calls == 1
                ? UniTask.FromException(new InvalidOperationException("unload")) : UniTask.CompletedTask);
            Assert.Throws<InvalidOperationException>(() => lease.UnloadAsync().GetAwaiter().GetResult());
            Assert.IsFalse(lease.IsUnloaded);
            lease.UnloadAsync().GetAwaiter().GetResult();
            Assert.IsTrue(lease.IsUnloaded);
        }
    }
}
