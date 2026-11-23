using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEditor;

namespace NowFrameWork.EditorTools
{
    /// <summary>为本地回归和 CI 提供同一套预检结果。</summary>
    public static class FrameworkRegressionPreflight
    {
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int reportIndex = Array.IndexOf(args, "-nwfPreflightReport") + 1;
            if (reportIndex == 0 || reportIndex >= args.Length)
                throw new ArgumentException("须提供 -nwfPreflightReport <报告路径>");

            var scenes = new List<FrameworkPreflight.Issue>(FrameworkPreflight.InspectEnabledBuildScenes(out int sceneCount));
            if (sceneCount == 0)
                scenes.Add(new FrameworkPreflight.Issue("SCENE004", "EditorBuildSettings", "没有启用的构建场景", null));

            var packages = new List<FrameworkPreflight.Issue>(
                FrameworkPackagePreflight.InspectInstalledPackages(out int packageCount, out int assemblyCount));
            if (packageCount == 0 || assemblyCount == 0)
                packages.Add(new FrameworkPreflight.Issue("DEP005", "Packages", "没有找到可预检的框架包或程序集", null));

            FrameworkPreflight.Print(scenes, "启用的构建场景 " + sceneCount + " 个");
            FrameworkPreflight.Print(packages, "框架包 " + packageCount + " 个，程序集 " + assemblyCount + " 个");

            int failures = (scenes.Count == 0 ? 0 : 1) + (packages.Count == 0 ? 0 : 1);
            string result = failures == 0 ? "Passed" : "Failed";
            var report = new XElement("test-run", new XAttribute("result", result),
                new XAttribute("total", 2), new XAttribute("passed", 2 - failures),
                new XAttribute("failed", failures), new XAttribute("skipped", 0),
                new XElement("test-suite", new XAttribute("type", "TestSuite"),
                    new XAttribute("name", "FrameworkPreflight"), new XAttribute("result", result),
                    CreateCase("BuildScenes", scenes), CreateCase("PackageDependencies", packages)));
            string path = Path.GetFullPath(args[reportIndex]);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            report.Save(path);
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static XElement CreateCase(string name, IReadOnlyList<FrameworkPreflight.Issue> issues)
        {
            var test = new XElement("test-case", new XAttribute("name", name),
                new XAttribute("result", issues.Count == 0 ? "Passed" : "Failed"));
            if (issues.Count != 0)
                test.Add(new XElement("failure", new XElement("message", string.Join("\n", issues))));
            return test;
        }
    }
}
