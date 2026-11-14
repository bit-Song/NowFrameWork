using System;
using System.Collections.Generic;

using NowFrameWork.Core;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Time;

namespace NowFrameWork.Core.Pooling
{
    /// <summary>
    /// C06 对象池：空闲列表 + 活跃集合 + 显式创建/销毁工厂。
    ///
    /// 与资源层的关系：池不关心资源从哪来，但**强制**把「创建可能持有租约、销毁必须释放」
    /// 收在 <see cref="IPoolItemFactory{T}"/> 里；池自身保证每个 Create 恰好对应一次 Destroy，
    /// 并通过 <see cref="Stats"/> 暴露累计数量供对账。
    ///
    /// 性能与 GC：
    /// - Get / Release 均为 O(1)，稳态零分配；
    /// - 活跃集合用于 O(1) 的重复归还检测；
    /// - 自动回收用换尾删除就地移除，不分配临时集合；
    /// - 取出与移除时会清空空闲槽引用，池不会长期持有已销毁对象。
    /// </summary>
    public sealed class ObjectPool<T> : IPool<T> where T : class
    {
        struct IdleEntry
        {
            public T Value;
            public double Since;
        }

        sealed class DelegateFactory : IPoolItemFactory<T>
        {
            readonly Func<T> _create;
            readonly Action<T> _destroy;

            public DelegateFactory(Func<T> create, Action<T> destroy)
            {
                _create = create;
                _destroy = destroy;
            }

            public T Create() => _create();

            public void Destroy(T item) => _destroy?.Invoke(item);
        }

        readonly List<IdleEntry> _idle = new List<IdleEntry>(16);
        int _idleCount;

        readonly HashSet<T> _active = new HashSet<T>();
        readonly IPoolItemFactory<T> _factory;
        readonly Func<T, bool> _isValid;
        readonly int _maxSize;               // 0 表示不限
        readonly double _autoRecycleSeconds; // <= 0 表示不自动回收
        readonly IClock _clock;
        readonly ILog _log;

        int _live;      // 创建 - 销毁
        int _created;   // 累计
        int _destroyed; // 累计
        bool _disposed;

        public ObjectPool(Func<T> create, Action<T> destroy = null, int maxSize = 0,
            double autoRecycleSeconds = 0d, int prewarm = 0, Func<T, bool> isValid = null, IClock clock = null, ILog log = null)
            : this(new DelegateFactory(create, destroy), maxSize, autoRecycleSeconds, prewarm, isValid, clock, log)
        {
        }

        /// <param name="factory">创建/销毁工厂；资源层在这里申请与释放租约。</param>
        /// <param name="maxSize">池容量上限（活跃+闲置）；0 表示不限。</param>
        /// <param name="autoRecycleSeconds">闲置多久后自动销毁；0 表示不回收。</param>
        /// <param name="prewarm">构造时预创建数量。</param>
        /// <param name="isValid">取出前校验对象仍有效（Unity 对象被外部销毁时用）；为空表示总是有效。</param>
        /// <param name="clock">自动回收所需时钟；启用回收时必填。</param>
        public ObjectPool(IPoolItemFactory<T> factory, int maxSize = 0,
            double autoRecycleSeconds = 0d, int prewarm = 0, Func<T, bool> isValid = null, IClock clock = null, ILog log = null)
        {
            if (autoRecycleSeconds > 0d && clock == null)
                throw new ArgumentNullException(nameof(clock), "启用自动回收必须提供时钟");

            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _maxSize = maxSize < 0 ? 0 : maxSize;
            _autoRecycleSeconds = autoRecycleSeconds;
            _isValid = isValid;
            _clock = clock;
            _log = log ?? Logs.Null;

            if (prewarm > 0) Prewarm(prewarm);
        }

        public int ActiveCount => _active.Count;
        public int InactiveCount => _idleCount;
        public int TotalCount => _live;
        public bool IsDisposed => _disposed;

        public PoolStats Stats => new PoolStats(_created, _destroyed, _active.Count, _idleCount, _maxSize);

        public T Get()
        {
            ThrowIfDisposed();

            while (_idleCount > 0)
            {
                T candidate = TakeIdle();
                if (candidate == null) continue;
                if (_isValid != null && !_isValid(candidate)) { DestroyItem(candidate); continue; }

                Spawn(candidate);
                return candidate;
            }

            if (_maxSize > 0 && _live >= _maxSize)
                throw new PoolExhaustedException("对象池已达上限 " + _maxSize + "，类型: " + typeof(T).Name);

            T created = CreateItem();
            Spawn(created);
            return created;
        }

        public bool TryGet(out T item)
        {
            ThrowIfDisposed();

            while (_idleCount > 0)
            {
                T candidate = TakeIdle();
                if (candidate == null) continue;
                if (_isValid != null && !_isValid(candidate)) { DestroyItem(candidate); continue; }

                Spawn(candidate);
                item = candidate;
                return true;
            }

            item = null;
            return false;
        }

        public void Release(T item)
        {
            ThrowIfDisposed();

            if (item == null) throw new PoolUsageException("归还 null 对象");

            if (!_active.Remove(item))
                throw new PoolUsageException("归还了不属于本池、或已经归还过的对象，类型: " + typeof(T).Name);

            (item as IPoolable)?.OnDespawn();

            // 超出上限：销毁而非无限囤积。显式行为，同时保证 Create/Destroy 仍一一对应
            if (_maxSize > 0 && _idleCount >= _maxSize)
            {
                DestroyItem(item);
                return;
            }

            PushIdle(item);
        }

        public int Prewarm(int count)
        {
            ThrowIfDisposed();
            if (count <= 0) return 0;

            int made = 0;
            for (int i = 0; i < count; i++)
            {
                if (_maxSize > 0 && _live >= _maxSize) break;
                PushIdle(CreateItem());
                made++;
            }

            return made;
        }

        public void Tick(double now)
        {
            if (_disposed || _autoRecycleSeconds <= 0d || _idleCount == 0) return;

            // 倒序 + 换尾删除：O(k)，不分配临时集合
            for (int i = _idleCount - 1; i >= 0; i--)
            {
                if (now - _idle[i].Since < _autoRecycleSeconds) continue;

                T expired = _idle[i].Value;
                RemoveIdleAt(i);
                DestroyItem(expired);
            }
        }

        public void Clear(bool includeActive)
        {
            for (int i = 0; i < _idleCount; i++) DestroyItem(_idle[i].Value);
            _idle.Clear();
            _idleCount = 0;

            if (!includeActive) return;

            foreach (T item in _active) DestroyItem(item);
            _active.Clear();
        }

        public void Dispose() => Dispose(force: false);

        public void Dispose(bool force)
        {
            if (_disposed) return;

            if (_active.Count > 0 && !force)
                throw new InvalidOperationException(
                    "池仍有 " + _active.Count + " 个未归还对象，拒绝释放；确认要强制销毁请用 Dispose(force: true)，类型: " + typeof(T).Name);

            Clear(includeActive: true);
            _disposed = true;
        }

        T CreateItem()
        {
            T obj = _factory.Create();
            if (obj == null) throw new PoolUsageException("创建函数返回 null，类型: " + typeof(T).Name);

            _created++;
            _live++;
            return obj;
        }

        void DestroyItem(T item)
        {
            if (item == null) return;

            _destroyed++;
            _live--;

            // 契约要求 Destroy 不抛，但这里仍兜底：
            // 一旦抛异常，后续对象的资源释放会被跳过，表现是"部分租约永远不归还"
            try
            {
                _factory.Destroy(item);
            }
            catch (Exception e)
            {
                if (_log.IsEnabled(LogLevel.Error))
                    _log.Write(LogLevel.Error, "C06", "销毁回调失败: " + typeof(T).Name + " -> " + e.GetType().Name + ": " + e.Message);
            }
        }

        void Spawn(T item)
        {
            _active.Add(item);
            (item as IPoolable)?.OnSpawn();
        }

        void PushIdle(T item)
        {
            var entry = new IdleEntry { Value = item, Since = _clock != null ? _clock.UnscaledNow : 0d };

            if (_idleCount < _idle.Count) _idle[_idleCount] = entry;
            else _idle.Add(entry);

            _idleCount++;
        }

        T TakeIdle()
        {
            int last = _idleCount - 1;
            T value = _idle[last].Value;

            _idle[last] = default;   // 清空引用
            _idleCount--;
            return value;
        }

        void RemoveIdleAt(int index)
        {
            int last = _idleCount - 1;
            _idle[index] = _idle[last];
            _idle[last] = default;
            _idleCount--;
        }

        void ThrowIfDisposed()
        {
            if (_disposed) throw new PoolUsageException("池已释放，类型: " + typeof(T).Name);
        }
    }
}
