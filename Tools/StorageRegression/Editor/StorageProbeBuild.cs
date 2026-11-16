using System;
using System.IO;
using NowFrameWork.Validation.Storage;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NowFrameWork.Validation.Editor
{
    public static class StorageProbeBuild
    {
        public static void Build()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            if (!File.Exists(Path.Combine(project, ".nw-storage-consumer")))
                throw new InvalidOperationException("只允许在隔离的存储消费工程构建");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("需要 -buildTarget Win64");
            PlayerSettings.companyName = "NowFrameWorkValidation";
            PlayerSettings.productName = "StorageRegression";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Low);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("StorageProbe").AddComponent<StoragePlayerProbe>();
            Directory.CreateDirectory("Assets/Scenes");
            const string scenePath = "Assets/Scenes/StorageProbe.unity";
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("保存回归场景失败");
            AssetDatabase.SaveAssets();
            string output = Path.GetFullPath(Path.Combine(project, "../Player/StorageRegression.exe"));
            var options = new BuildPlayerOptions { scenes = new[] { scenePath }, target = BuildTarget.StandaloneWindows64,
                locationPathName = output, options = BuildOptions.Development };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Storage Player build failed: " + report.summary.result);
            Debug.Log("[StorageBuild] PASS " + output);
        }
    }
}
