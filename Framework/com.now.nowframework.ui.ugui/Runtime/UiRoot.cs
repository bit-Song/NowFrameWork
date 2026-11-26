using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NowFrameWork.UI
{
    /// <summary>项目显式提供 Canvas、三层容器和 EventSystem；安全区仅在屏幕/视口变化时更新。</summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class UiRoot : MonoBehaviour
    {
        [SerializeField] Canvas _canvas;
        [SerializeField] RectTransform _safeArea;
        [SerializeField] Transform _pageLayer;
        [SerializeField] Transform _popupLayer;
        [SerializeField] Transform _toastLayer;
        [SerializeField] EventSystem _eventSystem;
        Rect _lastSafeArea, _lastViewport;
        bool _layoutReady;
        [SerializeField] Color _modalColor = new Color(0, 0, 0, 0.55f);
        Canvas _backdrop, _transitionBlocker;
        Action _backdropClicked;

        /// <summary>根 Canvas。</summary>
        public Canvas Canvas => _canvas;
        /// <summary>焦点与导航使用的 EventSystem。</summary>
        public EventSystem EventSystem => _eventSystem;
        /// <summary>共享遮罩是否可见。</summary>
        public bool BackdropVisible => _backdrop != null && _backdrop.gameObject.activeSelf;
        /// <summary>遮罩排序值。</summary>
        public int BackdropSortingOrder => _backdrop == null ? 0 : _backdrop.sortingOrder;
        /// <summary>切换过渡期间是否阻挡导航。</summary>
        public bool NavigationBlocked => _transitionBlocker != null && _transitionBlocker.gameObject.activeSelf;

        internal void PreparePresentation(Action backdropClicked)
        {
            if (_canvas.sortingOrder < -32768 || _canvas.sortingOrder > 8191)
                throw new InvalidOperationException("UiRoot sortingOrder 必须在 -32768–8191，为窗口与输入挡板保留排序范围。");
            _backdropClicked = backdropClicked;
            _backdrop = CreateOverlay("Shared Modal Backdrop", _modalColor, true);
            _transitionBlocker = CreateOverlay("Navigation Input Blocker", Color.clear, false);
        }

        Canvas CreateOverlay(string name, Color color, bool clickable)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            var rect = (RectTransform)go.transform; rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var canvas = go.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingLayerID = _canvas.sortingLayerID;
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = true;
            if (clickable)
            {
                var button = go.AddComponent<Button>(); button.targetGraphic = image; button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => _backdropClicked?.Invoke());
            }
            go.SetActive(false);
            return canvas;
        }

        internal void PresentBackdrop(bool visible, int order, bool blockAll)
        {
            if (_backdrop == null) return;
            _backdrop.sortingOrder = order;
            _backdrop.gameObject.SetActive(visible);
            _transitionBlocker.sortingOrder = _canvas.sortingOrder + 24575;
            _transitionBlocker.gameObject.SetActive(blockAll);
        }

        /// <summary>注入 Canvas、安全区与各层节点。</summary>
        public void Configure(Canvas canvas, RectTransform safeArea, Transform page, Transform popup, Transform toast,
            EventSystem eventSystem = null)
        {
            _canvas = canvas; _safeArea = safeArea; _pageLayer = page; _popupLayer = popup; _toastLayer = toast;
            _eventSystem = eventSystem; _layoutReady = false;
        }

        /// <summary>替换使用的 EventSystem。</summary>
        public void SetEventSystem(EventSystem eventSystem) => _eventSystem = eventSystem;

        /// <summary>取指定层的父节点。</summary>
        public Transform GetLayer(UiLayer layer)
        {
            Transform result;
            switch (layer)
            {
                case UiLayer.Page: result = _pageLayer; break;
                case UiLayer.Popup: result = _popupLayer; break;
                case UiLayer.Toast: result = _toastLayer; break;
                default: throw new ArgumentOutOfRangeException(nameof(layer));
            }
            if (result == null || !result.IsChildOf(transform)) throw new InvalidOperationException("UiRoot 层容器未配置: " + layer);
            return result;
        }

        /// <summary>校验必要节点，缺失时抛异常。</summary>
        public void Validate()
        {
            if (_canvas == null || _canvas.transform != transform || _safeArea == null || _safeArea.transform == transform || !_safeArea.IsChildOf(transform))
                throw new InvalidOperationException("UiRoot 必须配置自身 Canvas 和安全区容器");
            if (_canvas.renderMode == RenderMode.WorldSpace) throw new InvalidOperationException("首版 UiRoot 不支持 WorldSpace");
            if (_canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera == null)
                throw new InvalidOperationException("Camera UI 必须显式配置相机");
            if (GetLayer(UiLayer.Page) == GetLayer(UiLayer.Popup) || GetLayer(UiLayer.Page) == GetLayer(UiLayer.Toast)
                || GetLayer(UiLayer.Popup) == GetLayer(UiLayer.Toast)) throw new InvalidOperationException("UiRoot 三层容器必须不同");
            if (_eventSystem == null || !_eventSystem.enabled) throw new InvalidOperationException("项目必须显式提供启用的 EventSystem");
            if (!_eventSystem.gameObject.activeInHierarchy && !_eventSystem.transform.IsChildOf(transform))
                throw new InvalidOperationException("项目提供的 EventSystem 对象未激活");
            if (_eventSystem.transform.IsChildOf(transform))
                for (Transform node = _eventSystem.transform; node != transform; node = node.parent)
                    if (!node.gameObject.activeSelf) throw new InvalidOperationException("Root 内部 EventSystem 路径存在未激活的对象");
            int inputs = 0;
            foreach (BaseInputModule input in _eventSystem.GetComponents<BaseInputModule>()) if (input.enabled) inputs++;
            if (inputs > 1) throw new InvalidOperationException("同一 EventSystem 不能同时启用多套输入模块");
            int active = 0;
            foreach (EventSystem system in FindObjectsOfType<EventSystem>(true))
                if (system.enabled && (system.gameObject.activeInHierarchy || system.transform.IsChildOf(transform))) active++;
            if (active > 1) throw new InvalidOperationException("发现多个 EventSystem，拒绝启用 UI 根");
        }

        void Update()
        {
            if (_canvas == null || _safeArea == null) return;
            Rect viewport = new Rect(0, 0, Screen.width, Screen.height);
            if (_canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera != null) viewport = _canvas.worldCamera.pixelRect;
            Rect safe = Screen.safeArea;
            if (_layoutReady && safe == _lastSafeArea && viewport == _lastViewport) return;
            if (viewport.width <= 0 || viewport.height <= 0) return;
            _lastSafeArea = safe; _lastViewport = viewport; _layoutReady = true;
            _safeArea.anchorMin = new Vector2(Mathf.Clamp01((safe.xMin - viewport.xMin) / viewport.width), Mathf.Clamp01((safe.yMin - viewport.yMin) / viewport.height));
            _safeArea.anchorMax = new Vector2(Mathf.Clamp01((safe.xMax - viewport.xMin) / viewport.width), Mathf.Clamp01((safe.yMax - viewport.yMin) / viewport.height));
            _safeArea.offsetMin = _safeArea.offsetMax = Vector2.zero;
        }
    }
}
