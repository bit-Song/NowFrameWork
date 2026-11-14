using System;

using NowFrameWork.Core;

namespace NowFrameWork.Core.Pooling
{
    /// <summary>池的运行时统计。Created 与 Destroyed 用于**对账**：池释放后两者必须相等。</summary>
    public readonly struct PoolStats
    {
        public PoolStats(int created, int destroyed, int active, int inactive, int reserved, int maxSize)
        {
            Created = created;
            Destroyed = destroyed;
            Active = active;
            Inactive = inactive;
            Reserved = reserved;
            MaxSize = maxSize;
        }

        /// <summary>累计创建数。</summary>
        public int Created { get; }

        /// <summary>累计销毁数。池释放后应等于 <see cref="Created"/>。</summary>
        public int Destroyed { get; }

        public int Active { get; }
        public int Inactive { get; }

        /// <summary>正在异步创建中（已占名额、尚未交付）的数量。</summary>
        public int Reserved { get; }

        /// <summary>0 表示不限。</summary>
        public int MaxSize { get; }

        public override string ToString()
            => "pool created=" + Created + " destroyed=" + Destroyed + " active=" + Active + " inactive=" + Inactive;
    }

    /// <summary>
    /// 池的非泛型契约，供注册表统一驱动与统计。
    ///
    /// 生命周期与资源释放：
    /// 1. 池的销毁职责由创建时传入的销毁回调/工厂承担——**资源层应在那里释放租约**；
    /// 2. 池释放后不得再借用；仍有未归还对象时默认拒绝释放（force 才强制）；
    /// 3. 释放完成后 `Stats.Created` 应等于 `Stats.Destroyed`，用于发现漏释放。
    /// </summary>
    public interface IPool : IDisposable
    {
        int ActiveCount { get; }
        int InactiveCount { get; }
        int TotalCount { get; }

        bool IsDisposed { get; }

        PoolStats Stats { get; }

        /// <summary>按时间回收闲置对象；由 <see cref="PoolService"/> 每帧驱动。</summary>
        void Tick(double now);

        /// <summary>清空池内容但不释放池本身；includeActive 为真时连活跃对象一起销毁。</summary>
        void Clear(bool includeActive);

        /// <summary>释放池：销毁全部闲置对象；仍有活跃对象时抛异常，force 为真时一并销毁。</summary>
        void Dispose(bool force);
    }

    /// <summary>强类型池。</summary>
    public interface IPool<T> : IPool where T : class
    {
        /// <summary>取出对象；池空则按需创建，达到上限抛 <see cref="PoolExhaustedException"/>。</summary>
        T Get();

        /// <summary>只从闲置对象中取，不创建。</summary>
        bool TryGet(out T item);

        /// <summary>归还对象；重复归还、归还非本池对象、池已释放都会抛异常。</summary>
        void Release(T item);

        /// <summary>预创建闲置对象，返回实际创建数量。</summary>
        int Prewarm(int count);
    }
}
