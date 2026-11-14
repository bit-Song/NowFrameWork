using NowFrameWork.Core;

namespace NowFrameWork.Unity
{
    /// <summary>
    /// 真实时钟：直接读 UnityEngine.Time。
    /// 只读，不缓存——Unity 的时间每帧由引擎推进，缓存会与 TimeScale 变化不同步。
    /// </summary>
    public sealed class UnityClock : IClock
    {
        public double Now => UnityEngine.Time.timeAsDouble;

        public double UnscaledNow => UnityEngine.Time.unscaledTimeAsDouble;

        public float DeltaTime => UnityEngine.Time.deltaTime;

        public float UnscaledDeltaTime => UnityEngine.Time.unscaledDeltaTime;
    }
}