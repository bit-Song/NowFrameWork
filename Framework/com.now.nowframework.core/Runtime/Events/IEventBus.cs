using NowFrameWork.Core;

namespace NowFrameWork.Core.Events
{
    /// <summary>
    /// C04 事件总线。事件必须是 struct，避免装箱；派发路径零分配。
    ///
    /// 线程：只允许主线程。
    /// 派发语义：
    /// 1. 派发使用快照，handler 内部可以安全地订阅/退订；
    /// 2. 本轮派发中被退订的 handler 仍可能收到这一轮，下一轮不再收到；
    /// 3. 单个 handler 抛异常只记录日志，不影响其余 handler。
    /// </summary>
    public interface IEventBus
    {
        /// <summary>委托订阅；热路径请传静态方法，不要捕获闭包。</summary>
        IEventSubscription Subscribe<T>(EventHandler<T> handler) where T : struct;

        /// <summary>接口订阅；高频事件优先用它，订阅期也不产生委托。</summary>
        IEventSubscription Subscribe<T>(IEventHandler<T> handler) where T : struct;

        /// <summary>立即派发。</summary>
        void Publish<T>(in T evt) where T : struct;

        /// <summary>排队，等 <see cref="FlushDeferred"/> 统一派发（帧末调用）。</summary>
        void PublishDeferred<T>(in T evt) where T : struct;

        /// <summary>派发所有排队事件；每种事件类型内部保持 FIFO，跨类型不保证全局顺序。</summary>
        void FlushDeferred();

        /// <summary>清空全部订阅与排队事件。</summary>
        void Clear();
    }
}
