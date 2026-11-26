using DG.Tweening;
using UnityEngine;

namespace NowFrameWork.UI.Tweening
{
    /// <summary>共享的是效果数据，不存储节点、运行中的 Tween 或每个实例的姿态。</summary>
    [CreateAssetMenu(menuName = "NowFrameWork/UI/Tween Preset", fileName = "UiTweenPreset")]
    public sealed class UiTweenPreset : ScriptableObject
    {
        [SerializeField] UiTweenPhase _enter = new UiTweenPhase
        {
            Channels = UiTweenChannels.Fade | UiTweenChannels.Scale, Duration = 0.2f, Ease = Ease.OutCubic,
            Alpha = 0, Scale = new Vector3(0.9f, 0.9f, 1)
        };
        [SerializeField] UiTweenPhase _exit = new UiTweenPhase
        {
            Channels = UiTweenChannels.Fade | UiTweenChannels.Scale, Duration = 0.15f, Ease = Ease.InCubic,
            Alpha = 0, Scale = new Vector3(0.95f, 0.95f, 1)
        };
        /// <summary>显示动画参数。</summary>
        public UiTweenPhase Enter { get => _enter; set => _enter = value; }
        /// <summary>隐藏动画参数。</summary>
        public UiTweenPhase Exit { get => _exit; set => _exit = value; }
    }
}
