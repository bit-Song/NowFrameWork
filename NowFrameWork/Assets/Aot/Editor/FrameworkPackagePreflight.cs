using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UpmPackageInfo = UnityEditor.PackageManager.PackageInfo;

[assembly: InternalsVisibleTo("NowFrameWork.Preflight.Tests")]

namespace NowFrameWork.EditorTools
{
    /// <summary>检查 asmdef 显式依赖及其所属 UPM 包。</summary>
    public static class FrameworkPackagePreflight
    {
        const string Menu = "NowFrameWork/检查/预检框架包与程序集";
        const string FrameworkPrefix = "com.now.nowframework.";
        const string TestFramework = "com.unity.test-framework";

        [Serializable]
        sealed class Asmdef
        {
            public string name;
            public string[] references;
            public string[] optionalUnityReferences;
            public string[] defineConstraints;
            public string[] precompiledReferences;

            public bool IsTest => Contains(optionalUnityReferences, "TestAssemblies") ||
                Contains(defineConstraints, "UNITY_INCLUDE_TESTS") || Contains(precompiledReferences, "nunit.framework.dll");
        }

        internal sealed class AssemblyEntry
        {
            public string Name { get; }
            public string Guid { get; }
            public string Path { get; }
            public string Package { get; }
            public string[] References { get; }
            public bool IsTest { get; }
            public AssemblyDefinitionAsset Asset { get; }

            public AssemblyEntry(string name, string guid, string path, string package, string[] references = null,
                bool isTest = false, AssemblyDefinitionAsset asset = null)
            {
                Name = name;
                Guid = guid;
                Path = path;
                Package = package;
                References = references ?? Array.Empty<string>();
                IsTest = isTest;
                Asset = asset;
            }
        }

        [MenuItem(Menu, true)]
        static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        [MenuItem(Menu)]
        public static void CheckInstalledPackages()
        {
            var packages = UpmPackageInfo.GetAllRegisteredPackages();
            var owners = packages.Where(package => !string.IsNullOrEmpty(package.assetPath))
                .ToDictionary(package => package.assetPath, StringComparer.Ordinal);
            var dependencies = packages.Where(package => IsFrameworkPackage(package.name))
                .ToDictionary(package => package.name, package => package.dependencies.Select(item => item.name).ToArray(),
                    StringComparer.Ordinal);
            var assemblies = new List<AssemblyEntry>();
            var issues = new List<FrameworkPreflight.Issue>();

            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) continue;
                string packageName = null;
                if (path.StartsWith("Packages/", StringComparison.Ordinal))
                {
                    int rootEnd = path.IndexOf('/', "Packages/".Length);
                    if (rootEnd < 0 || !owners.TryGetValue(path.Substring(0, rootEnd), out var package)) continue;
                    packageName = package.name;
                }
                else if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;

                var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);
                Asmdef definition = null;
                try { if (asset != null) definition = JsonUtility.FromJson<Asmdef>(asset.text); }
                catch (ArgumentException) { }
                if (string.IsNullOrEmpty(definition?.name))
                {
                    if (IsFrameworkPackage(packageName))
                        issues.Add(new FrameworkPreflight.Issue("DEP004", path, "无法读取框架程序集定义", asset));
                    continue;
                }
                assemblies.Add(new AssemblyEntry(definition.name, AssetDatabase.AssetPathToGUID(path),
                    path, packageName, definition.references, definition.IsTest, asset));
            }

            issues.AddRange(Inspect(assemblies, dependencies));
            FrameworkPreflight.Print(issues, "已安装框架包 " + dependencies.Count + " 个，程序集 " +
                assemblies.Count(assembly => IsFrameworkPackage(assembly.Package)) + " 个");
        }

        internal static IEnumerable<FrameworkPreflight.Issue> Inspect(IReadOnlyList<AssemblyEntry> assemblies,
            IReadOnlyDictionary<string, string[]> dependencies)
        {
            var byName = assemblies.ToLookup(assembly => assembly.Name, StringComparer.Ordinal);
            var byGuid = assemblies.Where(assembly => !string.IsNullOrEmpty(assembly.Guid))
                .ToLookup(assembly => assembly.Guid, StringComparer.OrdinalIgnoreCase);

            foreach (var source in assemblies)
            {
                if (!IsFrameworkPackage(source.Package)) continue;
                dependencies.TryGetValue(source.Package, out var declared);
                foreach (string reference in source.References.Distinct(StringComparer.Ordinal))
                {
                    bool usesGuid = reference != null && reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase);
                    var matches = (usesGuid ? byGuid[reference.Substring(5)] : byName[reference]).Take(2).ToArray();
                    if (matches.Length == 0 && IsProjectDefaultAssembly(reference))
                    {
                        yield return Issue(source, "DEP001", "框架程序集引用了项目程序集 " + reference);
                        continue;
                    }

                    // 测试运行器由宿主的 Test Framework 提供；运行时程序集仍按普通依赖检查。
                    if (source.IsTest && IsTestRunner(matches.Length == 1 ? matches[0].Name : reference) &&
                        (matches.Length == 0 || (matches.Length == 1 && matches[0].Package == TestFramework))) continue;

                    if (matches.Length != 1)
                    {
                        yield return Issue(source, "DEP003", (matches.Length == 0 ? "找不到程序集引用 " :
                            "程序集引用无法唯一定位 ") + reference);
                        continue;
                    }

                    var target = matches[0];
                    if (target.Package == null)
                        yield return Issue(source, "DEP001", "框架程序集引用了项目程序集 " + target.Name +
                            " (" + target.Path + ")");
                    else if (target.Package != source.Package && !Contains(declared, target.Package))
                        yield return Issue(source, "DEP002", "引用 " + target.Name + " 需要在 " + source.Package +
                            " 的 package.json 声明包依赖 " + target.Package);
                }
            }
        }

        static FrameworkPreflight.Issue Issue(AssemblyEntry source, string code, string message)
            => new FrameworkPreflight.Issue(code, source.Path, message, source.Asset);

        static bool Contains(string[] values, string value) => values != null && Array.IndexOf(values, value) >= 0;

        static bool IsFrameworkPackage(string package) => package != null && package.StartsWith(FrameworkPrefix, StringComparison.Ordinal);

        static bool IsTestRunner(string assembly) => assembly == "UnityEngine.TestRunner" || assembly == "UnityEditor.TestRunner";

        static bool IsProjectDefaultAssembly(string assembly) => assembly == "Assembly-CSharp" ||
            assembly == "Assembly-CSharp-firstpass" || assembly == "Assembly-CSharp-Editor" ||
            assembly == "Assembly-CSharp-Editor-firstpass";
    }
}
