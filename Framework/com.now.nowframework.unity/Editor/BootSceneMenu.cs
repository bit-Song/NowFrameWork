using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NowFrameWork.Unity.EditorTools
{
    /// <summary>一键生成 Boot 场景：空场景 + 一个挂 GameBootstrap 的物体。见 ADR-020。</summary>
    public static class BootSceneMenu
    {
        const string SceneDirectory = "Assets/Scenes";
        const string ScenePath = SceneDirectory + "/Boot.unity";

        [MenuItem("NowFrameWork/创建 Boot 场景")]
        public static void CreateBootScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();

            if (!Directory.Exists(SceneDirectory)) Directory.CreateDirectory(SceneDirectory);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("保存场景失败: " + ScenePath);
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log("已创建 Boot 场景: " + ScenePath + "（记得加入 Build Settings）");
        }
    }
}