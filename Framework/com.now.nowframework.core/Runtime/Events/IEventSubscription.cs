using System;

namespace NowFrameWork.Core
{
    /// <summary>
    /// 订阅令牌。Dispose 幂等，重复释放不会影响其它订阅。
    /// 设计成 class 而非 struct：避免句柄被复制后"谁能释放"的歧义（订阅是冷路径，一次分配可接受）。
    /// </summary>
    public interface IEventSubscription : IDisposable
    {
        bool IsDisposed { get; }
    }
}