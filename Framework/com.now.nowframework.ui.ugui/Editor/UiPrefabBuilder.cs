using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NowFrameWork.UI.Editor
{
    public static class UiPrefabBuilder
    {
        [MenuItem("NowFrameWork/UI/创建最小 UI Prefab")]
        public static void CreateDefaults()
        {
            CreateRoot("Assets/Tests/UIIntegration/UiRoot.prefab");
            CreatePanel("Assets/Tests/UIIntegration/Panel.prefab");
            Debug.Log("[UI] 已创建最小 Prefab；Root 的 EventSystem 由项目入口显式注入。");
        }

        public static UiRoot CreateRoot(string path)
        {
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<UiRoot>(path)
                ?? throw new InvalidOperationException("已有 Prefab 缺少 UiRoot: " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var root = new GameObject("UiRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(UiRoot));
            try
            {
                Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
                RectTransform safe = Stretch("SafeArea", root.transform);
                RectTransform page = Stretch("Page", safe), popup = Stretch("Popup", safe), toast = Stretch("Toast", safe);
                root.GetComponent<UiRoot>().Configure(canvas, safe, page, popup, toast);
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<UiRoot>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static GameObject CreatePanel(string path)
        {
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var root = new GameObject("Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster), typeof(UiView));
            try
            {
                RectTransform rect = root.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                RectTransform box = Stretch("Background", root.transform);
                box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f); box.sizeDelta = new Vector2(800, 300);
                box.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.12f, 0.23f, 0.95f);
                RectTransform label = Stretch("Title", box);
                Text text = label.gameObject.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.text = "UI Framework\nOpen / Show / Hide / Close";
                text.fontSize = 42; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
