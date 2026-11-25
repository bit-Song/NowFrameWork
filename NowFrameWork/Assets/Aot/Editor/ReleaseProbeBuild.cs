using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NowFrameWork.EditorTools
{
    /// <summary>发布验收用 Mono Player：快速出包，供 HTTP / 强杀验收脚本使用。</summary>
    public static class ReleaseProbeBuild
    {
        const string Scene = "Assets/Scenes/BootIntegration.scene";
        const string Output = "Build/ReleaseProbe/NowFrameWork.exe";

        [MenuItem("NowFrameWork/Tests/构建发布验收 Player (Mono)")]
        public static void BuildMono()
        {
            string directory = Path.GetDirectoryName(Output);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException("构建失败: " + report.summary.result);

            Debug.Log("发布验收 Player 已生成: " + Output);
        }
    }
}
