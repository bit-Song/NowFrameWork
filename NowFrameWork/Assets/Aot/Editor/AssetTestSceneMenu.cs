using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NowFrameWork.EditorTools
{
    /// <summary>生成资源测试场景并加入 Build Settings，方便直接出包做真机验证。</summary>
    public static class AssetTestSceneMenu
    {
        const string ScenePath = "Assets/Scenes/AssetTest.scene";

        [MenuItem("NowFrameWork/Tests/创建资源测试场景")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("AssetLoadTest");
            go.AddComponent<NowFrameWork.AssetTest.AssetLoadTest>();

            string dir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("保存场景失败: " + ScenePath);
                return;
            }

            AddToBuildSettings();
            AssetDatabase.Refresh();

            Debug.Log("已创建 " + ScenePath + " 并加入 Build Settings（放在场景列表末尾）");
        }

        static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (scene.path == ScenePath) return;
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}