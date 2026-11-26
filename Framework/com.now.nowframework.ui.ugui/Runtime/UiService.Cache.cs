using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.UI
{
    public sealed partial class UiService
    {
        sealed class CachedView
        {
            public UiSession Session;
            public double ExpiresAt;
            public PlayerLoopTimer Timer;
            public bool Retiring;
            public UniTaskCompletionSource Eviction;

            public bool CanReuse(double now) => !Retiring && Session.View != null && now < ExpiresAt;

            public void StopTimer()
            {
                Timer?.Dispose();
                Timer = null;
            }
        }

        readonly UiCacheOptions _cacheOptions;
        readonly List<CachedView> _cache = new List<CachedView>();

        /// <summary>缓存中的面板数量。</summary>
        public int CachedCount
        {
            get
            {
                MainThreadGuard.Assert();
                double now = Time.realtimeSinceStartupAsDouble;
                int count = 0;
                foreach (CachedView entry in _cache) if (entry.CanReuse(now)) count++;
                return count;
            }
        }

        UiView TakeCachedView(int definitionId)
        {
            if (_cache.Count == 0) return null;
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = _cache.Count - 1; i >= 0; i--)
            {
                CachedView entry = _cache[i];
                if (entry.Session.Definition.Id != definitionId || !entry.CanReuse(now)) continue;
                _cache.RemoveAt(i);
                entry.StopTimer();
                UiView view = entry.Session.View;
                entry.Session.View = null;
                return view;
            }
            return null;
        }

        async UniTask<bool> TryCacheAsync(UiSession session)
        {
            if (_shuttingDown || _cacheOptions.Capacity == 0 || !session.Definition.CacheOnClose || session.View == null)
                return false;

            await session.View.CloseSessionAsync();
            if (_shuttingDown || session.View == null) return false;
            session.View.ParkInCache(session.VisualTransition);
            if (_shuttingDown || session.View == null) return false;
            if (_cache.Count >= _cacheOptions.Capacity)
            {
                // 先确认最早关闭的实例已销毁，再给新实例分配空闲额度。
                await EvictCachedAsync(_cache[0]);
            }
            if (_shuttingDown || _cache.Count >= _cacheOptions.Capacity || session.View == null) return false;

            var entry = new CachedView
            {
                Session = session,
                ExpiresAt = Time.realtimeSinceStartupAsDouble + _cacheOptions.IdleTimeout.TotalSeconds
            };
            if (Application.isPlaying)
                entry.Timer = PlayerLoopTimer.StartNew(_cacheOptions.IdleTimeout, false, DelayType.Realtime,
                    PlayerLoopTiming.Update, default, _ => EvictCachedAsync(entry).Forget(Debug.LogException), null);
            session.VisualTransition = null;
            _cache.Add(entry);
            FinishSession(session);
            return true;
        }

        UniTask EvictCachedAsync(CachedView entry)
        {
            if (!_cache.Contains(entry)) return UniTask.CompletedTask;
            if (entry.Eviction != null) return entry.Eviction.Task;
            entry.Retiring = true;
            entry.StopTimer();
            var completion = new UniTaskCompletionSource();
            entry.Eviction = completion;
            EvictCachedCoreAsync(entry, completion).Forget();
            return completion.Task;
        }

        async UniTask EvictCachedCoreAsync(CachedView entry, UniTaskCompletionSource completion)
        {
            try
            {
                await DestroyViewAsync(entry.Session);
                _cache.Remove(entry);
                ClearFault(entry.Session);
            }
            catch (Exception error)
            {
                if (!entry.Session.Faulted) { entry.Session.Faulted = true; _faultedCount++; }
                entry.Eviction = null;
                completion.TrySetException(error);
                return;
            }
            completion.TrySetResult();
        }

        /// <summary>回收全部空闲缓存。</summary>
        public UniTask TrimCacheAsync(CancellationToken ct = default)
        {
            MainThreadGuard.Assert("UiService.TrimCacheAsync");
            ct.ThrowIfCancellationRequested();
            return TrimCacheAndReportAsync().AttachExternalCancellation(ct);
        }

        async UniTask TrimCacheAndReportAsync()
        {
            var errors = new List<Exception>();
            await TrimCacheCoreAsync(errors);
            if (errors.Count != 0) throw new AggregateException("UI 缓存清理未完成", errors);
        }

        async UniTask TrimCacheCoreAsync(List<Exception> errors)
        {
            var snapshot = new List<CachedView>(_cache);
            foreach (CachedView entry in snapshot)
            {
                entry.Retiring = true;
                entry.StopTimer();
            }
            foreach (CachedView entry in snapshot)
            {
                try { await EvictCachedAsync(entry); }
                catch (Exception error) { errors.Add(error); }
            }
        }
    }
}
