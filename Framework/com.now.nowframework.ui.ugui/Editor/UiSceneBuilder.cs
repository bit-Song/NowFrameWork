using NowFrameWork.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NowFrameWork.UI.Editor
{
    /// <summary>在当前场景中创建标准 UiRoot，省去手动搭 Canvas 与三层节点。</summary>
    public static class UiSceneBuilder
    {
        [MenuItem("NowFrameWork/UI/在场景中创建 UiRoot", priority = 10)]
        public static void CreateInScene()
        {
            UiRoot existing = Object.FindObjectOfType<UiRoot>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.LogWarning("[UI] 当前场景已有 UiRoot：" + existing.name + "，已为你选中。", existing);
                return;
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

                Selection.activeGameObject = root;

                if (root.scene.IsValid()) EditorSceneManager.MarkSceneDirty(root.scene);
            }
            catch
            {
                Object.DestroyImmediate(root);
                throw;
            }

            if (Object.FindObjectOfType<EventSystem>() == null)
                Debug.LogWarning("[UI] 场景中没有 EventSystem。请添加 EventSystem 并挂 InputSystemUIInputModule" +
                    "（或项目现有的 UI 输入模块），否则点击与焦点导航不可用。");

            Debug.Log("[UI] 已在场景中创建 UiRoot（SafeArea / Page / Popup / Toast）。可直接拖到 Project 保存为 Prefab。", root);
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
    }
}
