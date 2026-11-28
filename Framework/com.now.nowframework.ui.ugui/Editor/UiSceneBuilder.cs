using NowFrameWork.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NowFrameWork.UI.Editor
{
    /// <summary>在场景中生成标准 UI 层级，省去手动搭 Canvas、分层与面板骨架。</summary>
    public static class UiSceneBuilder
    {
        const string InputModuleType = "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem";

        [MenuItem("NowFrameWork/UI/在场景中创建 UiRoot", priority = 10)]
        public static void CreateRootOnly()
        {
            UiRoot root = EnsureRoot();
            if (root == null) return;

            Selection.activeGameObject = root.gameObject;
            Debug.Log("[UI] 已在场景中创建 UiRoot（SafeArea / Page / Popup / Toast）。", root);

            if (Object.FindObjectOfType<EventSystem>() == null)
                Debug.LogWarning("[UI] 场景中没有 EventSystem。请添加 EventSystem 并挂 InputSystemUIInputModule" +
                    "（或项目现有的 UI 输入模块），否则点击与焦点导航不可用。");
        }

        [MenuItem("NowFrameWork/UI/在场景中创建基础 UI 层级", priority = 11)]
        public static void CreateBasicHierarchy()
        {
            UiRoot root = EnsureRoot();
            if (root == null) return;

            bool hasEventSystem = Object.FindObjectOfType<EventSystem>() != null || CreateEventSystem();

            UiView panel = FindPanel(root);
            if (panel == null)
            {
                panel = CreatePanel(root.GetLayer(UiLayer.Page));
                Selection.activeGameObject = panel.gameObject;
            }
            else
            {
                Selection.activeGameObject = panel.gameObject;
            }

            if (panel != null && panel.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);

            Debug.Log("[UI] 基础层级就绪：UiRoot + Panel（SafeArea / Layout / Header / Actions / Status）" +
                (hasEventSystem ? " + EventSystem。" : "；EventSystem 未创建，请手动添加。"), root);
        }

        [MenuItem("NowFrameWork/UI/在场景中创建面板", priority = 12)]
        public static void CreatePanelOnly()
        {
            UiRoot root = Object.FindObjectOfType<UiRoot>();
            if (root == null)
            {
                Debug.LogWarning("[UI] 场景中没有 UiRoot，先使用“在场景中创建 UiRoot”。");
                return;
            }

            UiView panel = CreatePanel(root.GetLayer(UiLayer.Page));
            Selection.activeGameObject = panel.gameObject;
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Debug.Log("[UI] 已创建面板骨架，可拖到 Project 保存为 Prefab。", panel);
        }

        static UiRoot EnsureRoot()
        {
            UiRoot existing = Object.FindObjectOfType<UiRoot>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.LogWarning("[UI] 当前场景已有 UiRoot：" + existing.name + "，复用它。", existing);
                return existing;
            }

            var root = new GameObject("UiRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UiRoot));

            Undo.RegisterCreatedObjectUndo(root, "Create UiRoot");

            try
            {
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                RectTransform safeArea = Stretch("SafeArea", root.transform);
                RectTransform page = Stretch("Page", root.transform);
                RectTransform popup = Stretch("Popup", root.transform);
                RectTransform toast = Stretch("Toast", root.transform);

                root.GetComponent<UiRoot>().Configure(canvas, safeArea, page, popup, toast);

                if (root.scene.IsValid()) EditorSceneManager.MarkSceneDirty(root.scene);
            }
            catch
            {
                Object.DestroyImmediate(root);
                throw;
            }

            return root.GetComponent<UiRoot>();
        }

        static UiView FindPanel(UiRoot root)
        {
            Transform page = root.GetLayer(UiLayer.Page);
            return page != null ? page.GetComponentInChildren<UiView>(true) : null;
        }

        /// <summary>创建面板骨架：根 Canvas/CanvasGroup/GraphicRaycaster/UiView + 安全区与基础内容。</summary>
        public static UiView CreatePanel(Transform parent, string name = "Panel")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup),
                typeof(GraphicRaycaster), typeof(UiView));

            Undo.RegisterCreatedObjectUndo(go, "Create UI Panel");

            try
            {
                var rect = (RectTransform)go.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;

                Canvas canvas = go.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 10;

                CanvasGroup group = go.GetComponent<CanvasGroup>();
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;

                RectTransform safe = Stretch("SafeArea", go.transform);
                safe.gameObject.AddComponent<UiSafeArea>();

                RectTransform layout = Stretch("Layout", safe);
                var vertical = layout.gameObject.AddComponent<VerticalLayoutGroup>();
                vertical.padding = new RectOffset(32, 32, 32, 32);
                vertical.spacing = 16f;
                vertical.childControlWidth = true;
                vertical.childControlHeight = false;
                vertical.childForceExpandWidth = true;
                vertical.childForceExpandHeight = false;

                CreateText("Header", layout, "PANEL", 40, 72f);

                RectTransform actions = CreateRow("Actions", layout, 64f);
                CreateButton("Primary", actions, new Color(0.25f, 0.45f, 0.75f));
                CreateButton("Secondary", actions, new Color(0.30f, 0.36f, 0.45f));

                CreateText("Status", layout, "Ready", 24, 48f);

                EditorSceneManager.MarkSceneDirty(go.scene);
                return go.GetComponent<UiView>();
            }
            catch
            {
                Object.DestroyImmediate(go);
                throw;
            }
        }

        static bool CreateEventSystem()
        {
            System.Type moduleType = System.Type.GetType(InputModuleType);
            if (moduleType == null)
            {
                Debug.LogWarning("[UI] 未检测到 Input System 的 UI 输入模块；请手动添加 EventSystem + InputSystemUIInputModule。");
                return false;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            go.AddComponent(moduleType);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return true;
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(rect.gameObject, "Create UiRoot");
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static Text CreateText(string name, Transform parent, string value, int size, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(go, "Create UI Panel");

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(0f, height);

            var text = go.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        static RectTransform CreateRow(string name, Transform parent, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            Undo.RegisterCreatedObjectUndo(go, "Create UI Panel");

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(0f, height);

            var horizontal = go.GetComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 16f;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = true;
            horizontal.childForceExpandHeight = true;
            return rect;
        }

        static void CreateButton(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(go, "Create UI Panel");

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            CreateText(name + " Label", go.transform, name, 26, 0f).alignment = TextAnchor.MiddleCenter;
        }
    }
}
