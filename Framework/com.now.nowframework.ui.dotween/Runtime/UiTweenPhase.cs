using System;
using DG.Tweening;
using UnityEngine;

namespace NowFrameWork.UI.Tweening
{
    /// <summary>Enter 的 Alpha/Offset/Scale 是隐藏起点，Exit 是隐藏终点；坐标和缩放均相对首次打开后的稳定姿态。</summary>
    [Serializable]
    public struct UiTweenPhase
    {
        /// <summary>参与动画的通道。</summary>
        public UiTweenChannels Channels;
        [Min(0)] public float Duration;
        [Min(0)] public float Delay;
        /// <summary>缓动曲线。</summary>
        public Ease Ease;
        /// <summary>是否使用自定义曲线。</summary>
        public bool UseCustomCurve;
        /// <summary>自定义曲线。</summary>
        public AnimationCurve Curve;
        [Range(0, 1)] public float Alpha;
        /// <summary>位移偏移。</summary>
        public Vector2 Offset;
        /// <summary>目标缩放。</summary>
        public Vector3 Scale;

        internal UiTweenPhase Snapshot()
        {
            const UiTweenChannels valid = UiTweenChannels.Fade | UiTweenChannels.Move | UiTweenChannels.Scale;
            if ((Channels & ~valid) != 0 || !Finite(Duration) || Duration < 0 || !Finite(Delay) || Delay < 0)
                throw new ArgumentException("UI 动画通道、时间或延迟无效。");
            if (!Finite(Alpha) || Alpha < 0 || Alpha > 1 || !Finite(Offset.x) || !Finite(Offset.y)
                || !Finite(Scale.x) || !Finite(Scale.y) || !Finite(Scale.z) || Scale.x < 0 || Scale.y < 0 || Scale.z < 0)
                throw new ArgumentException("UI 动画 alpha、位移或缩放无效。");
            UiTweenPhase result = this;
            if (UseCustomCurve)
            {
                if (Curve == null || Curve.length < 2) throw new ArgumentException("UI 自定义缓动曲线至少需要两个关键帧。");
                var keys = Curve.keys;
                foreach (var key in keys)
                    if (!Finite(key.time) || !Finite(key.value)) throw new ArgumentException("UI 缓动曲线包含非有限值。");
                if (!Mathf.Approximately(keys[0].time, 0) || !Mathf.Approximately(keys[0].value, 0)
                    || !Mathf.Approximately(keys[keys.Length - 1].time, 1) || !Mathf.Approximately(keys[keys.Length - 1].value, 1))
                    throw new ArgumentException("UI 缓动曲线必须从 (0,0) 到 (1,1)，中间允许回弹。");
                result.Curve = new AnimationCurve(keys) { preWrapMode = Curve.preWrapMode, postWrapMode = Curve.postWrapMode };
            }
            else if (!Enum.IsDefined(typeof(Ease), Ease) || Ease == DG.Tweening.Ease.INTERNAL_Custom
                || Ease == DG.Tweening.Ease.INTERNAL_Zero || Ease == DG.Tweening.Ease.Unset)
                throw new ArgumentException("请选择公开的 Ease 类型或有效的自定义曲线。");
            return result;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
