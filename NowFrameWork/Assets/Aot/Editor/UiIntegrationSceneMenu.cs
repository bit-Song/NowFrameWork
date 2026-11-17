using System;
using System.IO;
using Game;
using NowFrameWork.Boot;
using NowFrameWork.UI.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace NowFrameWork.EditorTools
{
    public static class UiIntegrationSceneMenu
    {
        [MenuItem("NowFrameWork/Tests/UI/创建 UI 启动测试场景")]
        public static void Create()
        {
            const string scenePath = "Assets/Scenes/UIIntegration.scene";
            var rootPrefab = UiPrefabBuilder.CreateRoot("Assets/Tests/UIIntegration/UiRoot.prefab");
            UiPrefabBuilder.CreatePanel("Assets/Tests/UIIntegration/Panel.prefab");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("请先创建 Addressables 配置");
            string guid = AssetDatabase.AssetPathToGUID("Assets/Tests/UIIntegration/Panel.prefab");
            var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = "UiProbe/Panel";
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            if (File.Exists(scenePath)) { Debug.Log("[UI] 已存在，请打开 " + scenePath); return; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Sample Camera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            camera.cullingMask = 0; // 示例提供背景帧；Overlay UI 本身不绑定这台相机。
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<EventSystem>();
            var host = new GameObject("UI Boot");
            var project = host.AddComponent<UiFlowSample>(); project.Configure(rootPrefab, eventSystem);
            var flow = host.AddComponent<BootFlow>();
            var serialized = new SerializedObject(flow);
            serialized.FindProperty("_projectEntry").objectReferenceValue = project;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Assets/Scenes");
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("保存 UI 场景失败");
            Debug.Log("[UI] Ready " + scenePath + "；先用 Use Asset Database 进行 Play 测试。");
        }
    }
}
