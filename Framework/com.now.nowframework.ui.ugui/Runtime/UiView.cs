using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NowFrameWork.UI
{
    /// <summary>仅主线程的 UGUI 视图。引擎启停不替代逻辑会话；原有 Canvas/输入配置只采集一次。</summary>
    [RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
    public class UiView : MonoBehaviour
    {
        Canvas[] _canvases;
        GraphicRaycaster[] _raycasters;
        Animator[] _animators;
        bool[] _canvasEnabled, _raycasterEnabled, _animatorEnabled;
        CanvasGroup _group;
        bool _interactable, _blocksRaycasts;
        float _alpha;
        bool _created, _opened, _disposed;
        bool _shown, _inputBlocked;
        int[] _sortingOffsets;
        GameObject _lastFocus;
        [SerializeField] Selectable _defaultFocus;
        CancellationTokenSource _session;
        CancellationTokenSource _visible;

        public int UiId { get; internal set; }
        public int Session { get; internal set; }
        public CancellationToken SessionToken => _session == null ? new CancellationToken(true) : _session.Token;
        public CancellationToken VisibleToken => _visible == null ? new CancellationToken(true) : _visible.Token;
        public CanvasGroup VisualGroup { get { Capture(); return _group; } }
        public bool InputBlocked => _inputBlocked;
        internal bool OwnsNavigationFocus { get; set; }
        public Selectable DefaultFocus { get => _defaultFocus; set => _defaultFocus = value; }

        /// <summary>用 UGUI 原生显式导航连接本面板可用控件。控件显隐或顺序变化后重新调用。</summary>
        public void ConfigureVerticalNavigation(params Selectable[] controls) => ConfigureNavigation(controls, vertical: true);

        /// <summary>用 UGUI 原生左右方向连接同一面板内的控件。</summary>
        public void ConfigureHorizontalNavigation(params Selectable[] controls) => ConfigureNavigation(controls, vertical: false);

        void ConfigureNavigation(Selectable[] controls, bool vertical)
        {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            var seen = new System.Collections.Generic.HashSet<Selectable>();
            foreach (Selectable control in controls)
                if (control == null || control.GetComponentInParent<UiView>(true) != this || !seen.Add(control))
                    throw new ArgumentException("导航控件必须是当前面板内互不重复的 Selectable。", nameof(controls));

            EventSystem system = EventSystem.current;
            GameObject previousSelected = system != null ? system.currentSelectedGameObject : null;
            GameObject lastSelected = previousSelected != null ? previousSelected : _lastFocus;
            Selectable previousControl = lastSelected != null ? lastSelected.GetComponent<Selectable>() : null;
            int previousIndex = previousControl != null ? System.Array.IndexOf(controls, previousControl) : -1;

            _defaultFocus = null;
            var navigable = new System.Collections.Generic.List<Selectable>(controls.Length);
            Selectable previous = null;

            foreach (Selectable control in controls)
            {
                // 保留另一方向的显式链接；本方向先清空，避免失效控件残留旧链路
                Navigation own = control.navigation;
                own.mode = Navigation.Mode.Explicit;

                if (vertical) { own.selectOnUp = null; own.selectOnDown = null; }
                else { own.selectOnLeft = null; own.selectOnRight = null; }

                control.navigation = own;

                if (!CanNavigate(control)) continue;

                navigable.Add(control);
                if (_defaultFocus == null) _defaultFocus = control;

                Navigation navigation = control.navigation;
                if (vertical) navigation.selectOnUp = previous;
                else navigation.selectOnLeft = previous;
                control.navigation = navigation;

                if (previous != null)
                {
                    Navigation previousNavigation = previous.navigation;
                    if (vertical) previousNavigation.selectOnDown = control;
                    else previousNavigation.selectOnRight = control;
                    previous.navigation = previousNavigation;
                }

                previous = control;
            }

            if (system == null || !_shown || _inputBlocked || !_group.interactable) return;
            if (previousSelected == null ? !OwnsNavigationFocus : !previousSelected.transform.IsChildOf(transform)) return;

            // 原焦点仍可导航：保持；已不可用（禁用/隐藏/祖先 CanvasGroup 关闭）则移到最近可用项，
            // 而不是清空焦点——否则禁用当前按钮后重建导航会丢失焦点。
            if (previousSelected != null && previousControl != null && navigable.Contains(previousControl))
            {
                _lastFocus = previousSelected;
                return;
            }

            Selectable replacement = previousSelected == null && navigable.Contains(previousControl)
                ? previousControl : FindNearestNavigable(controls, navigable, previousIndex);
            _lastFocus = replacement != null ? replacement.gameObject : null;
            system.SetSelectedGameObject(_lastFocus);
        }

        /// <summary>
        /// 控件是否可成为导航目标：自身启用且 interactable，面板内部的 CanvasGroup 允许交互。
        /// 面板根的 CanvasGroup 由显示/输入控制，不在此判断。
        /// </summary>
        bool CanNavigate(Selectable control)
        {
            if (control == null || !control.transform.IsChildOf(transform)) return false;

            // 不用 IsInteractable()/IsActive()：它们计入面板根 CanvasGroup 与 activeInHierarchy，
            // 面板隐藏或控件位于预制体资产时会把全部控件判死；根组状态由 ApplyInput 单独控制。
            if (!control.enabled || !control.interactable) return false;

            for (Transform current = control.transform; current != null && current != transform; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;

                // CanvasGroup 的 interactable 依赖 OnCanvasGroupChanged 刷新，运行时修改后可能滞后；直接查父链。
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group != null && !group.interactable) return false;
            }

            return true;
        }

        internal Selectable FindNearestNavigable(Selectable[] controls, System.Collections.Generic.List<Selectable> navigable, int previousIndex)
        {
            if (navigable.Count == 0) return null;
            if (previousIndex < 0) return _defaultFocus != null ? _defaultFocus : navigable[0];

            for (int step = 1; step <= controls.Length; step++)
            {
                int index = (previousIndex + step) % controls.Length;
                if (navigable.Contains(controls[index])) return controls[index];
            }

            return _defaultFocus != null ? _defaultFocus : navigable[0];
        }
        internal float ShownAlpha { get { Capture(); return _alpha; } }
        public float VisibleAlpha => ShownAlpha;

        internal IUiTransition ResolveTransition(IUiTransition fallback)
        {
            var transitions = GetComponents<UiTransitionBehaviour>();
            if (transitions.Length > 1) throw new InvalidOperationException("面板根只能配置一个 UiTransitionBehaviour。");
            return transitions.Length == 1 && transitions[0].enabled ? (IUiTransition)transitions[0] : fallback;
        }

        void Capture()
        {
            if (_group != null) return;
            _group = GetComponent<CanvasGroup>();
            _alpha = _group.alpha > 0 ? _group.alpha : 1; // Prefab 可预设透明隐藏，Show 仍须有可见目标。
            _interactable = _group.interactable; _blocksRaycasts = _group.blocksRaycasts;
            _canvases = GetComponentsInChildren<Canvas>(true);
            _raycasters = GetComponentsInChildren<GraphicRaycaster>(true);
            _animators = GetComponentsInChildren<Animator>(true);
            _canvasEnabled = new bool[_canvases.Length];
            _sortingOffsets = new int[_canvases.Length];
            int originalOrder = GetComponent<Canvas>().sortingOrder;
            for (int i = 0; i < _canvases.Length; i++)
                _sortingOffsets[i] = _canvases[i].sortingOrder - originalOrder;
            _raycasterEnabled = new bool[_raycasters.Length];
            _animatorEnabled = new bool[_animators.Length];
            for (int i = 0; i < _canvases.Length; i++) _canvasEnabled[i] = _canvases[i].transform == transform || _canvases[i].enabled;
            for (int i = 0; i < _raycasters.Length; i++) _raycasterEnabled[i] = _raycasters[i].enabled;
            for (int i = 0; i < _animators.Length; i++) _animatorEnabled[i] = _animators[i].enabled;
            foreach (var group in GetComponentsInChildren<CanvasGroup>(true))
                if (group.transform != transform && group.ignoreParentGroups)
                    throw new InvalidOperationException("子 CanvasGroup 不能用 ignoreParentGroups 绕过窗口输入控制: " + group.name);
        }

        internal async UniTask OpenSessionAsync(object args, CancellationToken ct)
        {
            Capture();
            if (!_created) { _created = true; OnCreate(); }
            _session = new CancellationTokenSource();
            _opened = true;
            await OnOpenAsync(args, ct);
            ct.ThrowIfCancellationRequested();
        }

        internal void PrepareHidden() { Capture(); ApplyHidden(); }

        internal void BeginVisibility(bool visible)
        {
            Capture();
            _shown = false;
            _group.interactable = false; _group.blocksRaycasts = false;
            for (int i = 0; i < _raycasters.Length; i++) if (_raycasters[i] != null) _raycasters[i].enabled = false;
            if (visible)
            {
                for (int i = 0; i < _canvases.Length; i++) if (_canvases[i] != null) _canvases[i].enabled = _canvasEnabled[i];
                for (int i = 0; i < _animators.Length; i++) if (_animators[i] != null) _animators[i].enabled = _animatorEnabled[i];
                gameObject.SetActive(true);
                OnShowing();
            }
            else
            {
                CancelVisible();
                ClearFocus();
                OnHiding();
            }
        }

        internal void CommitVisible()
        {
            _group.alpha = _alpha;
            _shown = true;
            ApplyInput();
            if (_visible == null) _visible = new CancellationTokenSource();
            OnShown();
        }

        internal void CommitHidden() { ApplyHidden(); OnHidden(); }

        internal void ApplyHidden()
        {
            Capture();
            _shown = false;
            CancelVisible();
            ClearFocus();
            _group.alpha = 0; _group.interactable = false; _group.blocksRaycasts = false;
            for (int i = 0; i < _canvases.Length; i++) if (_canvases[i] != null) _canvases[i].enabled = false;
            for (int i = 0; i < _raycasters.Length; i++) if (_raycasters[i] != null) _raycasters[i].enabled = false;
            for (int i = 0; i < _animators.Length; i++) if (_animators[i] != null) _animators[i].enabled = false;
        }

        internal async UniTask CloseSessionAsync()
        {
            ApplyHidden();
            if (_session != null && !_session.IsCancellationRequested) _session.Cancel();
            if (_opened) { await OnCloseAsync(); _opened = false; }
            _session?.Dispose(); _session = null;
        }

        internal void ParkInCache(IUiTransition transition)
        {
            if (transition is UiTransitionBehaviour behaviour && behaviour != null) behaviour.ResetForCache(this);
            _lastFocus = null;
            Session = 0;
            gameObject.SetActive(false);
        }

        internal void DisposeInstance()
        {
            if (_disposed) return;
            OnDispose();
            _disposed = true;
        }

        void CancelVisible()
        {
            if (_visible == null) return;
            var source = _visible; _visible = null;
            try { source.Cancel(); } finally { source.Dispose(); }
        }

        void ClearFocus()
        {
            EventSystem system = EventSystem.current;
            if (system != null && system.currentSelectedGameObject != null
                && system.currentSelectedGameObject.transform.IsChildOf(transform)) system.SetSelectedGameObject(null);
        }

        internal void SetInputBlocked(bool blocked)
        {
            Capture();
            if (blocked && !_inputBlocked)
            {
                var system = EventSystem.current;
                if (system != null && system.currentSelectedGameObject != null
                    && system.currentSelectedGameObject.transform.IsChildOf(transform))
                    _lastFocus = system.currentSelectedGameObject;
            }
            _inputBlocked = blocked;
            ApplyInput();
            if (blocked) ClearFocus();
        }

        void ApplyInput()
        {
            bool enabled = _shown && !_inputBlocked;
            _group.interactable = enabled && _interactable;
            _group.blocksRaycasts = enabled && _blocksRaycasts;
            for (int i = 0; i < _raycasters.Length; i++)
                if (_raycasters[i] != null) _raycasters[i].enabled = enabled && _raycasterEnabled[i];
        }

        internal void RestoreFocus()
        {
            var system = EventSystem.current;
            if (system == null || !_shown || _inputBlocked || !_group.interactable) return;
            var selected = system.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(transform))
            {
                var selectedControl = selected.GetComponent<Selectable>();
                if (selected.activeInHierarchy && (selectedControl == null || CanNavigate(selectedControl)))
                {
                    _lastFocus = selected;
                    return;
                }
                system.SetSelectedGameObject(null);
            }
            Selectable target = _lastFocus != null ? _lastFocus.GetComponent<Selectable>() : null;
            if (!CanNavigate(target)) target = _defaultFocus;
            if (!CanNavigate(target)) return;

            system.SetSelectedGameObject(target.gameObject);
            _lastFocus = target.gameObject;
        }

        internal void SetSorting(int order, int layer)
        {
            Capture();
            for (int i = 0; i < _canvases.Length; i++)
            {
                Canvas canvas = _canvases[i];
                if (canvas == null) continue;
                if (canvas.transform != transform && !canvas.overrideSorting) continue;
                int offset = canvas.transform == transform ? 0 : _sortingOffsets[i];
                if (offset < 0 || offset > 30) throw new InvalidOperationException("独立排序的子 Canvas 相对根的 sortingOrder 必须在 0–30 内: " + canvas.name);
                canvas.overrideSorting = true; canvas.sortingLayerID = layer; canvas.sortingOrder = order + offset;
            }
        }

        internal bool ConsumeBack() => OnBackRequested();
        /// <summary>同步消费返回请求；true 表示面板自行处理。异步确认由业务 TaskRunner 发起，不在此等待自身关闭。</summary>
        protected virtual bool OnBackRequested() => false;

        protected virtual void OnCreate() { }
        protected virtual UniTask OnOpenAsync(object args, CancellationToken ct) => UniTask.CompletedTask;
        protected virtual void OnShowing() { }
        protected virtual void OnShown() { }
        protected virtual void OnHiding() { }
        protected virtual void OnHidden() { }
        protected virtual UniTask OnCloseAsync() => UniTask.CompletedTask;
        protected virtual void OnDispose() { }

        protected virtual void OnDestroy()
        {
            try { CancelVisible(); } catch (Exception error) { Debug.LogException(error); }
            if (_session == null) return;
            try { _session.Cancel(); } catch (Exception error) { Debug.LogException(error); }
            finally { _session.Dispose(); _session = null; }
        }
    }
}
