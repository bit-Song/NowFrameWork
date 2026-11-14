using System;

using NowFrameWork.Core;

namespace NowFrameWork.Core.Time
{
    /// <summary>
    /// C05 计时器服务。所有计时器都由 <see cref="Update"/> 驱动，不依赖 Unity 协程。
    /// 分配：注册时产生一次委托与槽位占用；Update 路径零分配。
    /// </summary>
    public interface ITimerService
    {
        /// <summary>当前活跃计时器数量。</summary>
        int PendingCount { get; }

        /// <summary>一次性延迟（游戏时间）。</summary>
        TimerHandle Delay(double seconds, Action callback);

        /// <summary>一次性延迟（真实时间，不受时间缩放影响）。</summary>
        TimerHandle DelayUnscaled(double seconds, Action callback);

        /// <summary>循环触发（游戏时间）；immediate 为真时首帧立即触发一次。</summary>
        TimerHandle Interval(double seconds, Action callback, bool immediate = false);

        /// <summary>循环触发（真实时间）。</summary>
        TimerHandle IntervalUnscaled(double seconds, Action callback, bool immediate = false);

        /// <summary>取消计时器；句柄过期或已触发时返回 false。</summary>
        bool Cancel(in TimerHandle handle);

        /// <summary>取消全部计时器。</summary>
        void Clear();

        /// <summary>由驱动方每帧调用（通常是 Bootstrap 的 Update）。</summary>
        void Update();
    }
}
