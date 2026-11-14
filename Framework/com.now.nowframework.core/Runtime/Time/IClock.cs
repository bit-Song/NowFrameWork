using NowFrameWork.Core;

namespace NowFrameWork.Core.Time
{
    /// <summary>
    /// 可注入时钟。Core 不引用 UnityEngine.Time，平台包用真实实现，测试用手动实现。
    /// 两个时间轴：Now 受时间缩放影响（游戏时间），UnscaledNow 不受影响（UI / 暂停特效）。
    /// </summary>
    public interface IClock
    {
        /// <summary>游戏时间（秒），受 TimeScale 影响。</summary>
        double Now { get; }

        /// <summary>真实时间（秒），不受 TimeScale 影响。</summary>
        double UnscaledNow { get; }

        /// <summary>上一帧的游戏时间增量。</summary>
        float DeltaTime { get; }

        /// <summary>上一帧的真实时间增量。</summary>
        float UnscaledDeltaTime { get; }
    }
}
