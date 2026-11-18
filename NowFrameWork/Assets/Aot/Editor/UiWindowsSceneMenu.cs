using System;
using System.IO;
using Game;
using NowFrameWork.Boot;
using NowFrameWork.UI;
using NowFrameWork.UI.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NowFrameWork.EditorTools
{
    public static class UiWindowsSceneMenu
    {
        [MenuItem("NowFrameWork/Tests/UI/创建多窗口导航测试场景")]
        public static void Create()
        {
            const string scenePath = "Assets/Scenes/UIWindows.scene";
            const string folder = "Assets/Tests/UIWindows";
            var root = UiPrefabBuilder.CreateRoot(folder + "/UiRoot.prefab");
            CreateWindow(folder + "/Window.prefab");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("请先创建 Addressables 配置");
            string guid = AssetDatabase.AssetPathToGUID(folder + "/Window.prefab");
            var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = "UiWindows/Window"; EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            if (File.Exists(scenePath)) { Debug.Log("[UI] 已存在，请打开 " + scenePath); return; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Background Camera", typeof(Camera)).GetComponent<Camera>(); camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<EventSystem>();
            var host = new GameObject("UI Windows Boot"); var project = host.AddComponent<UiWindowsSample>(); project.Configure(root, events);
            var flow = host.AddComponent<BootFlow>(); var serialized = new SerializedObject(flow);
            serialized.FindProperty("_projectEntry").objectReferenceValue = project; serialized.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Assets/Scenes");
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("保存 UI 多窗口场景失败");
            Debug.Log("[UI] Ready " + scenePath);
        }

        public static GameObject CreateWindow(string path)
        {
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var go = new GameObject("Window", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster), typeof(UiWindowSampleView));
            try
            {
                Stretch((RectTransform)go.transform);
                var safe = Rect("SafeAreaContent", go.transform); safe.gameObject.AddComponent<UiSafeArea>();
                var motion = Rect("MotionRoot", safe); var background = motion.gameObject.AddComponent<Image>();
                var titleRect = Rect("Title", motion); titleRect.anchorMin = new Vector2(0.05f, 0.62f); titleRect.anchorMax = new Vector2(0.95f, 0.95f);
                Text title = Text(titleRect, "Window", 36);
                var next = Button("Next page", motion, 0); var settings = Button("Settings", motion, 1);
                var confirm = Button("Confirm", motion, 2); var back = Button("Back", motion, 3);
                go.GetComponent<UiWindowSampleView>().Configure(title, background, motion, next, settings, confirm, back);
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        static Button Button(string name, Transform parent, int index)
        {
            var rect = Rect(name, parent); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420, 55); rect.anchoredPosition = new Vector2(0, 25 - 70 * index);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.25f, 0.36f, 0.5f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            Text(Rect("Label", rect), name, 26); return button;
        }
        static Text Text(RectTransform rect, string value, int size)
        {
            var text = rect.gameObject.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value; text.fontSize = size; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            return text;
        }
        static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); Stretch(rect); return rect;
        }
        static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
