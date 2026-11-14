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
        // 团结工程的场景扩展名是 .scene（与 SampleScene.scene 保持一致）
        const string ScenePath = SceneDirectory + "/Boot.scene";

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