using NowFrameWork.Core.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Pooling;
using NowFrameWork.Core.Time;

namespace NowFrameWork.Assets
{
    /// <summary>
    /// 资源池：一份**池级保活租约** + 一个对象池。
    ///
    /// 释放顺序（[13 §9.4]）：
    /// 1. 销毁全部实例；
    /// 2. 有界轮询，等"实例真的没了"（Unity 伪空），**不依赖帧数也不依赖 PlayerLoop**；
    /// 3. 确认后才释放保活租约。
    ///
    /// 帧等待可注入：运行时用 `UniTask.NextFrame`，EditMode 测试注入立即完成，
    /// 这样"等销毁完成"这件事本身可测，而不是只能在真机上验。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class AssetPool<T> : IDisposable where T : UnityEngine.Object
    {
        readonly ObjectPool<T> _pool;
        readonly AssetLease<UnityEngine.Object> _keepAlive;
        readonly List<T> _tracked = new List<T>(16);
        readonly ILog _log;
        readonly int _settleFrames;
        readonly Func<UniTask> _frameWaiter;

        UniTaskCompletionSource _disposing;
        bool _disposed;

        AssetPool(AssetLease<UnityEngine.Object> keepAlive, Func<T> create, Action<T> destroy,
            IClock clock, ILog log, int maxSize, int settleFrames, Func<UniTask> frameWaiter)
        {
            _keepAlive = keepAlive;
            _log = log ?? Logs.Null;
            _settleFrames = settleFrames < 1 ? 1 : settleFrames;
            _frameWaiter = frameWaiter ?? DefaultWaiter;

            _pool = new ObjectPool<T>(
                () =>
                {
                    T item = create();
                    _tracked.Add(item);
                    return item;
                },
                destroy,
                maxSize,
                0d,
                0,
                null,
                clock,
                log);
        }

        static UniTask DefaultWaiter() => UniTask.NextFrame();

        public AssetKey Key => _keepAlive.Key;

        public string CanonicalKey => _keepAlive.CanonicalKey;

        public int ActiveCount => _pool.ActiveCount;

        public int InactiveCount => _pool.InactiveCount;

        /// <summary>创建池：先拿保活租约，再建内部对象池。</summary>
        public static async UniTask<AssetPool<T>> CreateAsync(AssetService service, AssetKey key,
            Func<T> create, Action<T> destroy, IClock clock, ILog log = null, int maxSize = 0,
            int settleFrames = 60, Func<UniTask> frameWaiter = null, CancellationToken ct = default)
        {
            MainThreadGuard.Assert("AssetPool.CreateAsync");
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (create == null) throw new ArgumentNullException(nameof(create));
            if (destroy == null) throw new ArgumentNullException(nameof(destroy));
            if (clock == null) throw new ArgumentNullException(nameof(clock));

            AssetLease<UnityEngine.Object> keepAlive =
                await service.AcquireCacheAsync<UnityEngine.Object>(key, ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                return new AssetPool<T>(keepAlive, create, destroy, clock, log, maxSize, settleFrames, frameWaiter);
            }
            catch
            {
                keepAlive.Dispose();
                throw;
            }
        }

        public T Rent() => _pool.Get();

        public UniTask<T> RentAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("AssetPool.RentAsync");
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(_pool.Get());
        }

        public void Release(T item) => _pool.Release(item);

        public void Tick(double now) => _pool.Tick(now);

        /// <summary>销毁全部实例 → 等它们真的消失 → 释放保活租约。</summary>
        public UniTask DisposeAsync()
        {
            MainThreadGuard.Assert("AssetPool.DisposeAsync");
            if (_disposed) return UniTask.CompletedTask;
            if (_disposing != null) return _disposing.Task;
            var completion = new UniTaskCompletionSource();
            _disposing = completion;
            DisposeCoreAsync(completion).Forget();
            return completion.Task;
        }

        async UniTask DisposeCoreAsync(UniTaskCompletionSource completion)
        {
            try
            {
                _pool.Dispose(force: true);

                if (!await UnityObjectLifecycle.WaitAllDestroyedAsync(_tracked, _settleFrames, _frameWaiter))
                    throw new TimeoutException("实例未销毁，保留资源租约；销毁完成后须重试 DisposeAsync: " + CanonicalKey);

                _keepAlive.Dispose();
                _tracked.Clear();
                _disposed = true;
                _disposing = null;
                completion.TrySetResult();
            }
            catch (Exception e)
            {
                _disposing = null;
                if (_log.IsEnabled(LogLevel.Error)) _log.Write(LogLevel.Error, "ASSET", e.Message);
                completion.TrySetException(e);
            }
        }

        public void Dispose() => DisposeAsync().Forget();

    }
}
