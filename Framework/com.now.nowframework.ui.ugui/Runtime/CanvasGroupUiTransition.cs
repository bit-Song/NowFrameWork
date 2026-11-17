using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>仅主线程的可中断淡入淡出。从当前 alpha 开始；await 退出后不再写属性，不依赖 DOTween。</summary>
    public sealed class CanvasGroupUiTransition : IUiTransition
    {
        readonly float _seconds;
        public CanvasGroupUiTransition(float seconds = 0.15f)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            _seconds = seconds;
        }
        public UniTask ShowAsync(UiView view, CancellationToken ct) => FadeAsync(view, view.ShownAlpha, ct);
        public UniTask HideAsync(UiView view, CancellationToken ct) => FadeAsync(view, 0, ct);
        async UniTask FadeAsync(UiView view, float target, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CanvasGroup group = view.VisualGroup;
            float start = group.alpha;
            double began = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - began < _seconds)
            {
                ct.ThrowIfCancellationRequested();
                if (view == null || group == null) throw new InvalidOperationException("UI 过渡目标已销毁");
                float fraction = (float)((Time.realtimeSinceStartupAsDouble - began) / _seconds);
                group.alpha = Mathf.Lerp(start, target, fraction);
                await UniTask.NextFrame(cancellationToken: ct);
            }
            ct.ThrowIfCancellationRequested();
            if (group == null) throw new InvalidOperationException("UI 过渡目标已销毁");
            group.alpha = target;
        }
    }
}
