using System;
using System.IO;
using Game;
using NowFrameWork.Boot;
using NowFrameWork.UI;
using NowFrameWork.UI.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace NowFrameWork.EditorTools
{
    /// <summary>生成框架漫游场景：BootFlow + 演示入口 + UI 根预制体 + EventSystem。</summary>
    public static class FrameworkTourSceneMenu
    {
        const string ScenePath = "Assets/Scenes/FrameworkTour.scene";
        const string WindowPath = "Assets/Tests/UIWindows/Window.prefab";
        const string RootPath = "Assets/Tests/UIWindows/UiRoot.prefab";
        const string WindowAddress = "UiWindows/Window";

        [MenuItem("NowFrameWork/Tests/创建框架漫游演示场景")]
        public static void CreateScene()
        {
            // 复用 UI 样例的预制体，并确保已登记 Addressable 地址
            UiWindowsSceneMenu.CreateWindow(WindowPath);
            EnsureAddress(WindowPath, WindowAddress);

            UiRoot uiRoot = UiPrefabBuilder.CreateRoot(RootPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Background Camera", typeof(Camera)).GetComponent<Camera>();
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);

            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule))
                .GetComponent<EventSystem>();

            var root = new GameObject("Framework Tour");
            BootFlow flow = root.AddComponent<BootFlow>();
            var tour = root.AddComponent<FrameworkTourSample>();
            tour.Configure(uiRoot, events);

            var serialized = new SerializedObject(flow);
            SerializedProperty entry = serialized.FindProperty("_projectEntry");
            if (entry == null) throw new InvalidOperationException("BootFlow 缺少 _projectEntry 字段。");

            entry.objectReferenceValue = tour;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            string directory = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("场景保存失败: " + ScenePath);

            AssetDatabase.Refresh();
            Debug.Log("已创建 " + ScenePath + "，Play 后展示 Prefab 实例与 UI 流程。");
        }

        static void EnsureAddress(string assetPath, string address)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("请先创建 Addressables 配置。");

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("找不到资源: " + assetPath);

            AddressableAssetEntry entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = address;

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);
            AssetDatabase.SaveAssets();
        }
    }
}
