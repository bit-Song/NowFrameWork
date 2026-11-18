using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>放在全屏面板的直接内容子节点。只在安全区或视口变化时更新；背景与共享遮罩留在安全区外。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiSafeArea : MonoBehaviour
    {
        RectTransform _rect;
        Canvas _canvas;
        Rect _lastSafe, _lastViewport;
        bool _ready;
        void OnEnable()
        {
            _rect = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>(); _canvas = canvas != null ? canvas.rootCanvas : null;
            _ready = false;
            Update();
        }
        void Update()
        {
            if (_canvas == null) return;
            Rect viewport = _canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera != null
                ? _canvas.worldCamera.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
            Rect safe = Screen.safeArea;
            if (_ready && safe == _lastSafe && viewport == _lastViewport || viewport.width <= 0 || viewport.height <= 0) return;
            _ready = true; _lastSafe = safe; _lastViewport = viewport;
            _rect.anchorMin = new Vector2(Mathf.Clamp01((safe.xMin - viewport.xMin) / viewport.width), Mathf.Clamp01((safe.yMin - viewport.yMin) / viewport.height));
            _rect.anchorMax = new Vector2(Mathf.Clamp01((safe.xMax - viewport.xMin) / viewport.width), Mathf.Clamp01((safe.yMax - viewport.yMin) / viewport.height));
            _rect.offsetMin = _rect.offsetMax = Vector2.zero;
        }
    }
}
