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

        public double Now => _now;
        public double UnscaledNow => _unscaledNow;
        public float DeltaTime => _delta;
        public float UnscaledDeltaTime => _unscaledDelta;

        float _timeScale = 1f;

        /// <summary>时间缩放；设为 0 可模拟暂停，负值按 0 处理（时间不倒退）。</summary>
        public float TimeScale
        {
            get => _timeScale;
            set => _timeScale = value < 0f ? 0f : value;
        }

        /// <summary>推进一帧：先更新增量，再累加两个时间轴。</summary>
        public void Advance(float unscaledDelta)
        {
            if (unscaledDelta < 0f) unscaledDelta = 0f;

            _unscaledDelta = unscaledDelta;
            _delta = unscaledDelta * TimeScale;
            _unscaledNow += unscaledDelta;
            _now += _delta;
        }
    }
}
