using System;
using System.IO;
using DG.Tweening;
using Game;
using NowFrameWork.Boot;
using NowFrameWork.Core.Threading;
using NowFrameWork.UI.Editor;
using NowFrameWork.UI.Tweening;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace NowFrameWork.EditorTools
{
    public static class UiTweenSceneMenu
    {
        const string Folder = "Assets/Tests/UITween";
        [MenuItem("NowFrameWork/Tests/UI/创建面板动画测试场景")]
        public static void Create()
        {
            MainThreadGuard.BindCurrentThread();
            const string scenePath = "Assets/Scenes/UITween.scene";
            var root = UiPrefabBuilder.CreateRoot(Folder + "/UiRoot.prefab");
            var fade = CreatePreset("Fade", UiTweenChannels.Fade, Vector2.zero, Vector3.one, Ease.OutCubic);
            var slide = CreatePreset("Slide", UiTweenChannels.Fade | UiTweenChannels.Move, new Vector2(240, 0), Vector3.one, Ease.OutCubic);
            var pop = CreatePreset("Pop", UiTweenChannels.Fade | UiTweenChannels.Scale, Vector2.zero, new Vector3(0.85f, 0.85f, 1), Ease.OutBack);
            CreateAnimatedPanel("Home", fade); CreateAnimatedPanel("Inventory", slide); CreateAnimatedPanel("Settings", pop); CreateAnimatedPanel("Confirm", pop);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("请先创建 Addressables 配置");
            foreach (string name in new[] { "Home", "Inventory", "Settings", "Confirm" })
            {
                string guid = AssetDatabase.AssetPathToGUID(Folder + "/" + name + ".prefab");
                var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
                entry.address = "UiTween/" + name;
            }
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            if (File.Exists(scenePath)) { Debug.Log("[UI] 已存在，请打开 " + scenePath); return; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Background Camera", typeof(Camera)).GetComponent<Camera>(); camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<EventSystem>();
            var host = new GameObject("UI Tween Boot"); var project = host.AddComponent<UiWindowsSample>(); project.Configure(root, events);
            project.ConfigurePrefabAddresses("UiTween/Home", "UiTween/Inventory", "UiTween/Settings", "UiTween/Confirm");
            var flow = host.AddComponent<BootFlow>(); var serialized = new SerializedObject(flow);
            serialized.FindProperty("_projectEntry").objectReferenceValue = project; serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("保存 UI 动画场景失败");
            Debug.Log("[UI] Ready " + scenePath + "：Home 淡入、Inventory 滑入、Settings/Confirm 缩放。");
        }

        static UiTweenPreset CreatePreset(string name, UiTweenChannels channels, Vector2 offset, Vector3 scale, Ease ease)
        {
            string path = Folder + "/" + name + ".asset";
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<UiTweenPreset>(path)
                ?? throw new InvalidOperationException("已有文件不是 UI 动画预设: " + path);
            var preset = ScriptableObject.CreateInstance<UiTweenPreset>();
            preset.Enter = new UiTweenPhase { Channels = channels, Duration = 0.24f, Ease = ease, Alpha = 0, Offset = offset, Scale = scale };
            preset.Exit = new UiTweenPhase { Channels = channels, Duration = 0.16f, Ease = Ease.InCubic, Alpha = 0, Offset = -offset, Scale = scale };
            AssetDatabase.CreateAsset(preset, path); return preset;
        }

        static void CreateAnimatedPanel(string name, UiTweenPreset preset)
        {
            string path = Folder + "/" + name + ".prefab";
            if (File.Exists(path)) return;
            UiWindowsSceneMenu.CreateWindow(path);
            var instance = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var motion = instance.transform.Find("SafeAreaContent/MotionRoot") as RectTransform;
                if (motion == null) throw new InvalidOperationException("新建样例缺少 MotionRoot");
                instance.AddComponent<DOTweenUiTransition>().Configure(preset, motion);
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
        }
    }
}
