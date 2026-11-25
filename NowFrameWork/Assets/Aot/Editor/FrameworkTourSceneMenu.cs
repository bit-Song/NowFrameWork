using System.IO;
using NowFrameWork.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NowFrameWork.EditorTools
{
    /// <summary>生成一个最小可运行的框架漫游场景：GameBootstrap + BootFlow + Tour 入口。</summary>
    public static class FrameworkTourSceneMenu
    {
        const string ScenePath = "Assets/Scenes/FrameworkTour.scene";

        [MenuItem("NowFrameWork/Tests/创建框架漫游演示场景")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Framework Tour");
            BootFlow boot = root.AddComponent<BootFlow>();

            var tour = root.AddComponent<Game.FrameworkTourSample>();

            var serialized = new SerializedObject(boot);
            SerializedProperty entry = serialized.FindProperty("_projectEntry");
            if (entry == null) throw new System.InvalidOperationException("BootFlow 缺少 _projectEntry 字段。");

            entry.objectReferenceValue = tour;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            string directory = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new System.InvalidOperationException("场景保存失败: " + ScenePath);

            AssetDatabase.Refresh();
            Debug.Log("已创建 " + ScenePath + "，Play 后按步骤显示框架能力。");
        }
    }
}
