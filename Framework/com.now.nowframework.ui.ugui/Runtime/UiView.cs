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
        bool _created, _opened, _closed, _disposed;
        CancellationTokenSource _session;
        CancellationTokenSource _visible;

        public int UiId { get; internal set; }
        public int Session { get; internal set; }
        public CancellationToken SessionToken => _session == null ? new CancellationToken(true) : _session.Token;
        public CancellationToken VisibleToken => _visible == null ? new CancellationToken(true) : _visible.Token;
        public CanvasGroup VisualGroup { get { Capture(); return _group; } }
        internal float ShownAlpha { get { Capture(); return _alpha; } }

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
            _raycasterEnabled = new bool[_raycasters.Length];
            _animatorEnabled = new bool[_animators.Length];
            for (int i = 0; i < _canvases.Length; i++) _canvasEnabled[i] = _canvases[i].transform == transform || _canvases[i].enabled;
            for (int i = 0; i < _raycasters.Length; i++) _raycasterEnabled[i] = _raycasters[i].enabled;
            for (int i = 0; i < _animators.Length; i++) _animatorEnabled[i] = _animators[i].enabled;
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
            _group.interactable = _interactable; _group.blocksRaycasts = _blocksRaycasts;
            for (int i = 0; i < _raycasters.Length; i++) if (_raycasters[i] != null) _raycasters[i].enabled = _raycasterEnabled[i];
            if (_visible == null) _visible = new CancellationTokenSource();
            OnShown();
        }

        internal void CommitHidden() { ApplyHidden(); OnHidden(); }

        internal void ApplyHidden()
        {
            Capture();
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
            if (_opened && !_closed) { await OnCloseAsync(); _closed = true; }
            if (!_disposed) { OnDispose(); _disposed = true; }
            _session?.Dispose(); _session = null;
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
