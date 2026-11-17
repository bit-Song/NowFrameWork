using System;
using System.IO;
using NowFrameWork.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NowFrameWork.EditorTools
{
    public static class FrameworkApiSampleSceneMenu
    {
        const string ScenePath = "Assets/Scenes/FrameworkApiSample.scene";

        [MenuItem("NowFrameWork/Tests/API/创建完整流程测试场景（不含UI）")]
        public static void Create()
        {
            if (File.Exists(ScenePath)) { Debug.Log("[API] 已存在，请打开 " + ScenePath); return; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("FrameworkApiSample");
            var entry = root.AddComponent<FrameworkApiSample>();
            var flow = root.AddComponent<BootFlow>();
            var serialized = new SerializedObject(flow);
            serialized.FindProperty("_projectEntry").objectReferenceValue = entry;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Assets/Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("API 场景保存失败");
            AssetDatabase.SaveAssets();
            Debug.Log("[API] 已创建 " + ScenePath + "。Play 后在 FrameworkApiSample 组件菜单运行测试。");
        }
    }
}
