using NowFrameWork.Core.Threading;
using NowFrameWork.Core.Time;

using NowFrameWork.Core;

namespace NowFrameWork.Unity
{
    /// <summary>
    /// 真实时钟：直接读 UnityEngine.Time。
    /// 只读，不缓存——Unity 的时间每帧由引擎推进，缓存会与 TimeScale 变化不同步。
    /// </summary>
    public sealed class UnityClock : IClock
    {
        /// <summary>游戏时间（受 TimeScale 影响）。</summary>
        public double Now
        {
            get
            {
                MainThreadGuard.Assert("UnityClock.Now");
                return UnityEngine.Time.timeAsDouble;
            }
        }

        /// <summary>真实时间。</summary>
        public double UnscaledNow
        {
            get
            {
                MainThreadGuard.Assert("UnityClock.UnscaledNow");
                return UnityEngine.Time.unscaledTimeAsDouble;
            }
        }

        /// <summary>游戏时间增量。</summary>
        public float DeltaTime
        {
            get
            {
                MainThreadGuard.Assert("UnityClock.DeltaTime");
                return UnityEngine.Time.deltaTime;
            }
        }

        /// <summary>真实时间增量。</summary>
        public float UnscaledDeltaTime
        {
            get
            {
                MainThreadGuard.Assert("UnityClock.UnscaledDeltaTime");
                return UnityEngine.Time.unscaledDeltaTime;
            }
        }
    }
}
