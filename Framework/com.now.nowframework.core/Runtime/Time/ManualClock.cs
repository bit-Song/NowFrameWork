using NowFrameWork.Core.Threading;
using NowFrameWork.Core;

namespace NowFrameWork.Core.Time
{
    /// <summary>
    /// 手动推进的时钟：测试与固定步长模拟用。不依赖任何 Unity API。
    /// 分配：Advance 只改字段，不产生垃圾。
    /// </summary>
    public sealed class ManualClock : IClock
    {
        double _now;
        double _unscaledNow;
        float _delta;
        float _unscaledDelta;

        /// <summary>当前游戏时间（受 TimeScale 影响）。</summary>
        public double Now
        {
            get
            {
                MainThreadGuard.Assert("ManualClock.Now");
                return _now;
            }
        }
        /// <summary>当前真实时间（不受 TimeScale 影响）。</summary>
        public double UnscaledNow
        {
            get
            {
                MainThreadGuard.Assert("ManualClock.UnscaledNow");
                return _unscaledNow;
            }
        }
        /// <summary>上一帧的游戏时间增量。</summary>
        public float DeltaTime
        {
            get
            {
                MainThreadGuard.Assert("ManualClock.DeltaTime");
                return _delta;
            }
        }
        /// <summary>上一帧的真实时间增量。</summary>
        public float UnscaledDeltaTime
        {
            get
            {
                MainThreadGuard.Assert("ManualClock.UnscaledDeltaTime");
                return _unscaledDelta;
            }
        }

        float _timeScale = 1f;

        /// <summary>时间缩放；设为 0 可模拟暂停，负值按 0 处理（时间不倒退）。</summary>
        public float TimeScale
        {
            get { MainThreadGuard.Assert("ManualClock.TimeScale"); return _timeScale; }
            set { MainThreadGuard.Assert("ManualClock.TimeScale"); _timeScale = value < 0f ? 0f : value; }
        }

        /// <summary>推进一帧：先更新增量，再累加两个时间轴。</summary>
        public void Advance(float unscaledDelta)
        {
            MainThreadGuard.Assert("ManualClock.Advance");
            if (unscaledDelta < 0f) unscaledDelta = 0f;

            _unscaledDelta = unscaledDelta;
            _delta = unscaledDelta * TimeScale;
            _unscaledNow += unscaledDelta;
            _now += _delta;
        }
    }
}
