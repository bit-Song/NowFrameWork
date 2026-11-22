using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Core;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Platform;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.Audio
{
    /// <summary>Application audio. Call from the main thread and tick while running.</summary>
    public sealed class AudioModule : IAppModule, ITickable
    {
        sealed class ClipUse
        {
            public AssetLease<AudioClip> Lease;
            public int Voices;
            public bool ReleaseReported;
        }

        sealed class Voice
        {
            public AudioSource Source;
            public ClipUse Clip;
            public bool Started;
        }

        readonly IAssetService _assets;
        readonly IAppLifecycle _lifecycle;
        readonly Transform _parent;
        readonly ILog _log;
        readonly int _maxEffects;
        readonly List<Voice> _effects = new List<Voice>();
        readonly List<ClipUse> _clips = new List<ClipUse>();

        GameObject _root;
        Voice _music;
        CancellationTokenSource _lifetime;
        UniTaskCompletionSource _closing;
        bool _running;
        bool _paused;
        bool _background;
        int _pending;
        long _musicRequest;
        long _effectGeneration;
        float _musicVolume = 1f;
        float _effectsVolume = 1f;

        public AudioModule(IAssetService assets, IAppLifecycle lifecycle, Transform parent = null,
            int maxEffects = 16, ILog log = null)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            if (maxEffects < 1) throw new ArgumentOutOfRangeException(nameof(maxEffects));
            _maxEffects = maxEffects;
            _parent = parent;
            _log = log ?? Logs.Null;
        }

        public string Id => "audio";
        public IReadOnlyList<string> DependsOn => Array.Empty<string>();
        public bool IsPaused => _paused || _background;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert();
            ct.ThrowIfCancellationRequested();
            if (_root != null || _lifetime != null) throw new InvalidOperationException("Audio is already initialized.");

            try
            {
                _root = new GameObject("NowFrameWork Audio");
                _root.transform.SetParent(_parent, false);
                _music = CreateVoice("Music", loop: true, _musicVolume);
                _lifetime = new CancellationTokenSource();
                return UniTask.CompletedTask;
            }
            catch
            {
                // 部分初始化失败必须回滚，否则 _root != null 会让模块永久无法再次初始化
                if (_lifetime != null) { _lifetime.Dispose(); _lifetime = null; }
                if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }
                _music = null;
                throw;
            }
        }

        public UniTask StartAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert();
            ct.ThrowIfCancellationRequested();
            if (_root == null || _running || _lifetime.IsCancellationRequested)
                throw new InvalidOperationException("Audio is not ready to start.");
            _background = IsBackground(_lifecycle.State);
            _lifecycle.StateChanged += OnLifecycleChanged;
            _running = true;
            return UniTask.CompletedTask;
        }

        /// <summary>Replaces the looping BGM after the new clip loads. Returns false if superseded.</summary>
        public UniTask<bool> PlayMusicAsync(object key, CancellationToken ct = default)
        {
            CheckRunning();
            return PlayAsync(key, true, ++_musicRequest, ct);
        }

        /// <summary>Plays one sound. Returns false at the voice limit or after StopEffects.</summary>
        public UniTask<bool> PlayEffectAsync(object key, CancellationToken ct = default)
        {
            CheckRunning();
            return PlayAsync(key, false, _effectGeneration, ct);
        }

        async UniTask<bool> PlayAsync(object key, bool music, long request, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            linked.Token.ThrowIfCancellationRequested();
            _pending++;
            try
            {
                var lease = await _assets.AcquireAsync<AudioClip>(key, linked.Token);
                ClipUse clip = Adopt(lease);
                linked.Token.ThrowIfCancellationRequested();
                if (music ? request != _musicRequest : request != _effectGeneration) return false;
                Voice voice = music ? _music : FindEffectVoice();
                if (voice == null) return false;
                if (voice.Clip != null) Detach(voice);
                Attach(voice, clip);
                return true;
            }
            finally
            {
                _pending--;
                ReleaseUnused(throwOnError: false);
            }
        }

        ClipUse Adopt(AssetLease<AudioClip> lease)
        {
            foreach (var clip in _clips)
            {
                if (clip.Voices == 0 || clip.Lease.CanonicalKey != lease.CanonicalKey) continue;
                lease.Dispose();
                return clip;
            }

            var adopted = new ClipUse { Lease = lease };
            _clips.Add(adopted);
            return adopted;
        }

        Voice FindEffectVoice()
        {
            foreach (var voice in _effects)
                if (voice.Clip == null) return voice;
            if (_effects.Count >= _maxEffects) return null;
            var created = CreateVoice("Effect", loop: false, _effectsVolume);
            _effects.Add(created);
            return created;
        }

        Voice CreateVoice(string name, bool loop, float volume)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.volume = volume;
            return new Voice { Source = source };
        }

        void Attach(Voice voice, ClipUse clip)
        {
            voice.Source.clip = clip.Lease.Asset;
            voice.Clip = clip;
            clip.Voices++;
            if (!IsPaused) ResumeVoice(voice);
        }

        static void Detach(Voice voice)
        {
            if (voice.Source != null)
            {
                voice.Source.Stop();
                voice.Source.clip = null;
            }
            voice.Clip.Voices--;
            voice.Clip = null;
            voice.Started = false;
        }

        static void ResumeVoice(Voice voice)
        {
            if (voice.Started) voice.Source.UnPause();
            else
            {
                voice.Source.Play();
                voice.Started = true;
            }
        }

        public void StopMusic()
        {
            CheckRunning();
            _musicRequest++;
            if (_music.Clip != null) Detach(_music);
            ReleaseUnused(throwOnError: true);
        }

        public void StopEffects()
        {
            CheckRunning();
            _effectGeneration++;
            foreach (var voice in _effects)
                if (voice.Clip != null) Detach(voice);
            ReleaseUnused(throwOnError: true);
        }

        public void SetVolumes(float music, float effects)
        {
            MainThreadGuard.Assert();
            if (float.IsNaN(music)) throw new ArgumentOutOfRangeException(nameof(music));
            if (float.IsNaN(effects)) throw new ArgumentOutOfRangeException(nameof(effects));
            _musicVolume = Mathf.Clamp01(music);
            _effectsVolume = Mathf.Clamp01(effects);
            if (_music != null && _music.Source != null) _music.Source.volume = _musicVolume;
            foreach (var voice in _effects)
                if (voice.Source != null) voice.Source.volume = _effectsVolume;
        }

        public void SetPaused(bool paused)
        {
            CheckRunning();
            bool previous = IsPaused;
            _paused = paused;
            ApplyPause(previous);
        }

        static bool IsBackground(AppState state) => state == AppState.Background || state == AppState.Quitting;

        void OnLifecycleChanged(AppState state)
        {
            bool previous = IsPaused;
            _background = IsBackground(state);
            ApplyPause(previous);
        }

        void ApplyPause(bool previous)
        {
            if (previous == IsPaused) return;
            ApplyPause(_music);
            foreach (var voice in _effects) ApplyPause(voice);
        }

        void ApplyPause(Voice voice)
        {
            // 事件回调可能晚于关闭清理到达，_music 已被置空
            if (voice == null || voice.Clip == null || voice.Source == null) return;
            if (IsPaused) voice.Source.Pause();
            else ResumeVoice(voice);
        }

        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            MainThreadGuard.Assert();
            if (!_running) return;
            if (!IsPaused)
            {
                foreach (var voice in _effects)
                    if (voice.Clip != null && voice.Started &&
                        (voice.Source == null || !voice.Source.isPlaying)) Detach(voice);
            }
            ReleaseUnused(throwOnError: false);
        }

        void ReleaseUnused(bool throwOnError)
        {
            List<Exception> failures = null;
            for (int i = _clips.Count - 1; i >= 0; i--)
            {
                ClipUse clip = _clips[i];
                if (clip.Voices != 0) continue;
                try
                {
                    clip.Lease.Dispose();
                    _clips.RemoveAt(i);
                }
                catch (Exception error)
                {
                    if (throwOnError) (failures ??= new List<Exception>()).Add(error);
                    else if (!clip.ReleaseReported)
                    {
                        clip.ReleaseReported = true;
                        _log.Write(LogLevel.Warn, "AUDIO", "Audio lease release failed and will be retried: " + error.Message);
                    }
                }
            }
            if (failures != null) throw new AggregateException("Audio lease cleanup failed; retry stopping.", failures);
        }

        public async UniTask ShutdownAsync(CancellationToken ct)
        {
            try { await DisposeAsync(); }
            catch (Exception error)
            {
                if (_log.IsEnabled(LogLevel.Error))
                    _log.Write(LogLevel.Error, "AUDIO", "Audio shutdown incomplete; retry AudioModule.DisposeAsync: " + error);
            }
        }

        /// <summary>Stops all voices and releases resources. Retry if the provider fails to release.</summary>
        public UniTask DisposeAsync()
        {
            MainThreadGuard.Assert();
            if (_closing != null) return _closing.Task;
            if (_lifetime == null) return UniTask.CompletedTask;
            var done = new UniTaskCompletionSource();
            _closing = done;
            CloseAsync(done).Forget();
            return done.Task;
        }

        async UniTask CloseAsync(UniTaskCompletionSource done)
        {
            try
            {
                if (_running) _lifecycle.StateChanged -= OnLifecycleChanged;
                _running = false;
                _lifetime.Cancel();
                while (_pending != 0) await UniTask.NextFrame();
                if (_music.Clip != null) Detach(_music);
                foreach (var voice in _effects)
                    if (voice.Clip != null) Detach(voice);
                ReleaseUnused(throwOnError: true);
                UnityEngine.Object.Destroy(_root);
                _root = null;
                _music = null;
                _effects.Clear();
                _lifetime.Dispose();
                _lifetime = null;
                _paused = false;
                _background = false;
                _closing = null;
                done.TrySetResult();
            }
            catch (Exception error)
            {
                _closing = null;
                done.TrySetException(error);
            }
        }

        void CheckRunning()
        {
            MainThreadGuard.Assert();
            if (!_running || _root == null) throw new InvalidOperationException("Audio is not running.");
        }
    }
}
