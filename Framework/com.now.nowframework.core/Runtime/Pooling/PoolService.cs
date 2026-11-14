using System;
using System.Collections.Generic;

namespace NowFrameWork.Core
{
    /// <summary>
    /// 池注册表：按字符串键创建/复用池，每帧驱动自动回收，并负责在资源卸载时整池释放。
    ///
    /// 与资源层的配合：资源卸载前必须 `DisposePool(资源键)`——池释放会调用工厂的 Destroy，
    /// 从而释放该资源的所有租约；如果漏掉这一步，表现是「资源卸载后池里还留着它的实例」，
    /// 或者租约计数永远不归零。
    ///
    /// 分配：Tick 只做下标遍历，不产生临时集合。
    /// </summary>
    public sealed class PoolService : IDisposable
    {
        readonly IClock _clock;
        readonly ILog _log;
        readonly Dictionary<string, IPool> _byKey = new Dictionary<string, IPool>(StringComparer.Ordinal);
        readonly List<IPool> _all = new List<IPool>(8);   // 遍历用，避免字典枚举期间增删抛异常

        bool _disposed;

        public PoolService(IClock clock, ILog log = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? Logs.Null;
        }

        public int PoolCount => _all.Count;

        public ObjectPool<T> GetOrCreate<T>(string key, Func<T> create, Action<T> destroy = null,
            int maxSize = 0, double autoRecycleSeconds = 0d, int prewarm = 0, Func<T, bool> isValid = null)
            where T : class
        {
            ThrowIfDisposed();
            return GetOrCreate(key, new DelegateFactory<T>(create, destroy), maxSize, autoRecycleSeconds, prewarm, isValid);
        }

        public ObjectPool<T> GetOrCreate<T>(string key, IPoolItemFactory<T> factory,
            int maxSize = 0, double autoRecycleSeconds = 0d, int prewarm = 0, Func<T, bool> isValid = null)
            where T : class
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("池键不能为空", nameof(key));

            if (_byKey.TryGetValue(key, out IPool existing))
            {
                if (existing is ObjectPool<T> typed) return typed;
                throw new InvalidOperationException("键 " + key + " 已被其它类型的池占用");
            }

            var pool = new ObjectPool<T>(factory, maxSize, autoRecycleSeconds, prewarm, isValid, _clock, _log);
            _byKey.Add(key, pool);
            _all.Add(pool);
            return pool;
        }

        public bool TryGetPool<T>(string key, out ObjectPool<T> pool) where T : class
        {
            if (_byKey.TryGetValue(key, out IPool existing) && existing is ObjectPool<T> typed)
            {
                pool = typed;
                return true;
            }

            pool = null;
            return false;
        }

        /// <summary>释放并移除一个池；资源卸载时调用。仍有未归还对象需要 force。</summary>
        public bool DisposePool(string key, bool force = false)
        {
            if (!_byKey.TryGetValue(key, out IPool pool)) return false;

            pool.Dispose(force);
            _byKey.Remove(key);

            int index = _all.IndexOf(pool);
            if (index >= 0)
            {
                int last = _all.Count - 1;
                _all[index] = _all[last];
                _all.RemoveAt(last);
            }

            return true;
        }

        /// <summary>每帧驱动：回收各池中闲置超时的对象。用真实时间，暂停时照常回收。</summary>
        public void Tick()
        {
            if (_disposed) return;

            double now = _clock.UnscaledNow;

            for (int i = 0; i < _all.Count; i++)
            {
                // 单个池的回收失败不能拖垮其它池
                try { _all[i].Tick(now); }
                catch (Exception e)
                {
                    if (_log.IsEnabled(LogLevel.Error))
                        _log.Write(LogLevel.Error, "C06", "池回收失败 -> " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        public void ClearAll(bool includeActive)
        {
            for (int i = 0; i < _all.Count; i++) _all[i].Clear(includeActive);
        }

        public void Dispose()
        {
            if (_disposed) return;

            for (int i = 0; i < _all.Count; i++) _all[i].Dispose(force: true);
            _all.Clear();
            _byKey.Clear();
            _disposed = true;
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PoolService));
        }

        /// <summary>把委托适配成工厂，避免调用方为简单场景写一个类。</summary>
        sealed class DelegateFactory<T> : IPoolItemFactory<T> where T : class
        {
            readonly Func<T> _create;
            readonly Action<T> _destroy;

            public DelegateFactory(Func<T> create, Action<T> destroy)
            {
                _create = create ?? throw new ArgumentNullException(nameof(create));
                _destroy = destroy;
            }

            public T Create() => _create();

            public void Destroy(T item) => _destroy?.Invoke(item);
        }
    }
}