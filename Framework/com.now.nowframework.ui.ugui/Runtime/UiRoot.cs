using System;
using UnityEngine;
using UnityEngine.EventSystems;

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

        public Canvas Canvas => _canvas;
        public EventSystem EventSystem => _eventSystem;

        public void Configure(Canvas canvas, RectTransform safeArea, Transform page, Transform popup, Transform toast,
            EventSystem eventSystem = null)
        {
            _canvas = canvas; _safeArea = safeArea; _pageLayer = page; _popupLayer = popup; _toastLayer = toast;
            _eventSystem = eventSystem; _layoutReady = false;
        }

        public void SetEventSystem(EventSystem eventSystem) => _eventSystem = eventSystem;

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
