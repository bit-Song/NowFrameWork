using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.UI.Tweening
{
    /// <summary>
    /// 仅主线程。根节点配置共享预设，节点引用保存在每个 Prefab；只终止自己创建的 Tween。
    /// 每次会话捕获稳定姿态和预设快照；反向从当前值继续，取消不跳到终点。
    /// </summary>
    public class DOTweenUiTransition : UiTransitionBehaviour
    {
        [SerializeField] UiTweenPreset _preset;
        [SerializeField] RectTransform _motionRoot;
        [SerializeField] CanvasGroup _fadeTarget;
        UiView _view;
        CanvasGroup _group;
        UiTweenPhase _enter, _exit;
        Vector2 _shownPosition;
        Vector3 _shownScale;
        float _shownAlpha;
        bool _captured, _hidden = true;
        Tween _active;

        public bool IsPlaying => _active != null && _active.IsActive();
        public UiTweenPreset Preset => _preset;
        public RectTransform MotionRoot => _motionRoot;
        protected virtual UpdateType TweenUpdateType => UpdateType.Normal;

        public void Configure(UiTweenPreset preset, RectTransform motionRoot = null, CanvasGroup fadeTarget = null)
        {
            MainThreadGuard.Assert("DOTweenUiTransition.Configure");
            if (_captured || IsPlaying) throw new InvalidOperationException("过渡已经开始，不能在当前会话中切换配置。");
            _preset = preset; _motionRoot = motionRoot; _fadeTarget = fadeTarget;
        }

        public override UniTask ShowAsync(UiView view, CancellationToken ct) => PlayAsync(view, true, ct);
        public override UniTask HideAsync(UiView view, CancellationToken ct) => PlayAsync(view, false, ct);

        void Capture(UiView view)
        {
            if (_captured)
            {
                if (_view != view) throw new InvalidOperationException("过渡组件不能被其他视图复用。");
                return;
            }
            if (view == null || view.gameObject != gameObject) throw new InvalidOperationException("过渡组件必须挂在 UiView 同一根节点。");
            if (_preset == null) throw new InvalidOperationException("面板的 DOTween 过渡未配置 Preset。");
            _enter = _preset.Enter.Snapshot(); _exit = _preset.Exit.Snapshot();
            bool needsMotion = ((_enter.Channels | _exit.Channels) & (UiTweenChannels.Move | UiTweenChannels.Scale)) != 0;
            if (needsMotion && (_motionRoot == null || _motionRoot == view.transform || !_motionRoot.IsChildOf(view.transform)))
                throw new InvalidOperationException("位移/缩放必须显式引用本面板内部的 MotionRoot，不能直接驱动面板根。");
            _group = _fadeTarget != null ? _fadeTarget : view.VisualGroup;
            if (!_group.transform.IsChildOf(view.transform)) throw new InvalidOperationException("Fade Target 必须属于当前面板。");
            _view = view;
            _shownAlpha = _group == view.VisualGroup ? view.VisibleAlpha : (_group.alpha > 0 ? _group.alpha : 1);
            if (_motionRoot != null) { _shownPosition = _motionRoot.anchoredPosition; _shownScale = _motionRoot.localScale; }
            _captured = true;
        }

        async UniTask PlayAsync(UiView view, bool show, CancellationToken ct)
        {
            MainThreadGuard.Assert("DOTweenUiTransition.PlayAsync");
            ct.ThrowIfCancellationRequested();
            if (IsPlaying) throw new InvalidOperationException("旧过渡尚未退出，禁止并发写入同一面板。");
            Capture(view); ValidateTargets();
            UiTweenPhase phase = show ? _enter : _exit;
            bool fade = (phase.Channels & UiTweenChannels.Fade) != 0;
            bool move = (phase.Channels & UiTweenChannels.Move) != 0;
            bool scale = (phase.Channels & UiTweenChannels.Scale) != 0;
            if (show)
            {
                // 服务先打开绘制、在结束时提交输入；非根淡入或纯位移动画也需要根 alpha 可见。
                if (_group != view.VisualGroup || !fade) view.VisualGroup.alpha = view.VisibleAlpha;
                UiTweenChannels ownedChannels = _enter.Channels | _exit.Channels;
                if (!fade && (ownedChannels & UiTweenChannels.Fade) != 0) _group.alpha = _shownAlpha;
                if (!move && (ownedChannels & UiTweenChannels.Move) != 0) _motionRoot.anchoredPosition = _shownPosition;
                if (!scale && (ownedChannels & UiTweenChannels.Scale) != 0) _motionRoot.localScale = _shownScale;
                if (_hidden)
                {
                    if (fade) _group.alpha = _shownAlpha * phase.Alpha;
                    if (move) _motionRoot.anchoredPosition = _shownPosition + phase.Offset;
                    if (scale) _motionRoot.localScale = Vector3.Scale(_shownScale, phase.Scale);
                }
            }
            _hidden = false;
            float fromAlpha = _group.alpha, toAlpha = show ? _shownAlpha : _shownAlpha * phase.Alpha;
            Vector2 fromPosition = _motionRoot != null ? _motionRoot.anchoredPosition : Vector2.zero;
            Vector2 toPosition = show ? _shownPosition : _shownPosition + phase.Offset;
            Vector3 fromScale = _motionRoot != null ? _motionRoot.localScale : Vector3.one;
            Vector3 toScale = show ? _shownScale : Vector3.Scale(_shownScale, phase.Scale);
            Action<float> apply = progress =>
            {
                ValidateTargets();
                if (fade) _group.alpha = Mathf.Clamp01(Mathf.LerpUnclamped(fromAlpha, toAlpha, progress));
                if (move) _motionRoot.anchoredPosition = Vector2.LerpUnclamped(fromPosition, toPosition, progress);
                if (scale) _motionRoot.localScale = Vector3.LerpUnclamped(fromScale, toScale, progress);
            };
            if (phase.Duration == 0 && phase.Delay == 0) { apply(1); _hidden = !show; return; }
            bool completed = false;
            var done = new UniTaskCompletionSource();
            Tween owned = null;
            try
            {
                float progress = 0;
                owned = DG.Tweening.DOTween.To(() => progress, value => { progress = value; apply(value); }, 1, phase.Duration)
                    .SetUpdate(TweenUpdateType, true).SetDelay(phase.Delay).SetAutoKill(false).SetRecyclable(false).SetTarget(this).Pause();
                if (phase.UseCustomCurve) owned.SetEase(phase.Curve); else owned.SetEase(phase.Ease);
                owned.OnComplete(() => { completed = true; done.TrySetResult(); });
                owned.OnKill(() => done.TrySetResult());
                _active = owned;
                owned.Play();
                await done.Task.AttachExternalCancellation(ct);
                ct.ThrowIfCancellationRequested();
                if (!completed) throw new InvalidOperationException("UI Tween 被外部停止或目标失效。");
                ValidateTargets();
                apply(1);
                _hidden = !show;
            }
            finally
            {
                // 即使 ct 来自后台线程，返回前也须在主线程停止实际写入。
                await UniTask.SwitchToMainThread();
                if (owned != null && owned.IsActive()) owned.Kill(false);
                if (ReferenceEquals(_active, owned)) _active = null;
            }
        }

        void ValidateTargets()
        {
            if (_view == null || _group == null) throw new InvalidOperationException("UI 动画目标已销毁。");
            if (((_enter.Channels | _exit.Channels) & (UiTweenChannels.Move | UiTweenChannels.Scale)) != 0 && _motionRoot == null)
                throw new InvalidOperationException("MotionRoot 已销毁。");
        }

        protected override void OnStoredInCache(UiView view)
        {
            if (!_captured) return;
            UiTweenChannels channels = _enter.Channels | _exit.Channels;
            if (_group != null && _group != view.VisualGroup && (channels & UiTweenChannels.Fade) != 0)
                _group.alpha = _shownAlpha;
            if (_motionRoot != null)
            {
                if ((channels & UiTweenChannels.Move) != 0) _motionRoot.anchoredPosition = _shownPosition;
                if ((channels & UiTweenChannels.Scale) != 0) _motionRoot.localScale = _shownScale;
            }
            _captured = false;
            _hidden = true;
            _enter = _exit = default;
            _view = null;
            _group = null;
        }

        protected virtual void OnDestroy()
        {
            Tween owned = _active; _active = null;
            if (owned != null && owned.IsActive()) owned.Kill(false);
        }
    }
}
