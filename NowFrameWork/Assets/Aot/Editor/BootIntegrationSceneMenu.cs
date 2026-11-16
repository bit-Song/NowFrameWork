using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NowFrameWork.EditorTools
{
    public static class BootIntegrationSceneMenu
    {
        const string ScenePath = "Assets/Scenes/BootIntegration.scene";
        [MenuItem("NowFrameWork/Tests/Boot/创建启动整合场景")]
        public static void Create()
        {
            if (File.Exists(ScenePath)) { Debug.Log("[BootScene] 已存在：" + ScenePath); return; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再创建 Boot 整合场景");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("BootIntegration").AddComponent<NowFrameWork.Boot.BootFlow>();
            Directory.CreateDirectory("Assets/Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("保存 Boot 整合场景失败");
            AssetDatabase.SaveAssets();
            Debug.Log("[BootScene] Ready " + ScenePath);
        }
    }
}
