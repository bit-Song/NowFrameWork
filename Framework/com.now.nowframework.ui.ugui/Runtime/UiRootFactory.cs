using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NowFrameWork.UI
{
    /// <summary>运行时创建 UiRoot 与 EventSystem，避免项目手动摆层级。</summary>
    public static class UiRootFactory
    {
        /// <summary>创建标准 UiRoot：Canvas + SafeArea + Page / Popup / Toast。</summary>
        public static UiRoot Create(Transform parent = null, EventSystem eventSystem = null, int sortingOrder = 0)
        {
            var root = new GameObject("UiRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UiRoot));

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform safeArea = Stretch("SafeArea", root.transform);
            RectTransform page = Stretch("Page", root.transform);
            RectTransform popup = Stretch("Popup", root.transform);
            RectTransform toast = Stretch("Toast", root.transform);

            UiRoot view = root.GetComponent<UiRoot>();
            view.Configure(canvas, safeArea, page, popup, toast, eventSystem);

            if (parent != null) root.transform.SetParent(parent, false);

            return view;
        }

        /// <summary>场景已有 EventSystem 则复用，否则创建并挂可用的输入模块。</summary>
        public static EventSystem EnsureEventSystem(Transform parent = null)
        {
            EventSystem existing = UnityEngine.Object.FindObjectOfType<EventSystem>();

            if (existing != null) return existing;

            var go = new GameObject("EventSystem", typeof(EventSystem));

            Type inputSystemModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");

            if (inputSystemModule != null) go.AddComponent(inputSystemModule);
            else go.AddComponent<StandaloneInputModule>();

            if (parent != null) go.transform.SetParent(parent, false);

            return go.GetComponent<EventSystem>();
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
