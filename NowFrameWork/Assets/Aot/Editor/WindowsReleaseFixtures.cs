using System;
using System.IO;
using System.Security.Cryptography;
using NowFrameWork.Assets;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace NowFrameWork.EditorTools
{
    public static class WindowsReleaseFixtures
    {
        const string Source = "Assets/Tests/TemporaryReleaseFixture";
        public const string Root = "Build/ReleaseRecovery/fixtures";

        [MenuItem("NowFrameWork/Tests/生成 Windows 发布恢复样例")]
        public static void Build()
        {
            if (AssetDatabase.IsValidFolder(Source)) throw new InvalidOperationException("临时发布样例目录已存在");
            AssetDatabase.CreateFolder("Assets/Tests", "TemporaryReleaseFixture");
            try
            {
                BuildRelease("v1");
                BuildRelease("v2");
                BuildRelease("v3"); // 坏文件/回退测试需要"与当前确认版本不同 ID"的发布集
            }
            finally { AssetDatabase.DeleteAsset(Source); }
        }

        static void BuildRelease(string id)
        {
            string source = Source + "/" + id + ".txt";
            File.WriteAllText(source, id);
            AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
            string output = Path.GetFullPath(Path.Combine(Root, id));
            Directory.CreateDirectory(output);
            string bundle = "release-" + id + ".bundle";
            var build = new AssetBundleBuild { assetBundleName = bundle, assetNames = new[] { source } };
            if (BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64) == null)
                throw new InvalidOperationException("构建发布样例 Bundle 失败: " + id);

            var catalog = new ContentCatalogData(new[]
            {
                new ContentCatalogDataEntry(typeof(IAssetBundleResource), "{NowFrameWork.ReleaseRoot}/" + bundle,
                    typeof(AssetBundleProvider).FullName, new object[] { bundle }, extraData:
                    new AssetBundleRequestOptions { BundleName = bundle }),
                new ContentCatalogDataEntry(typeof(TextAsset), source.ToLowerInvariant(),
                    typeof(BundledAssetProvider).FullName, new object[] { "ReleaseProbe/Config" },
                    new object[] { bundle })
            }, id + "-catalog");
            catalog.ResourceProviderData.Add(ObjectInitializationData.CreateSerializedInitializationData<AssetBundleProvider>());
            catalog.ResourceProviderData.Add(ObjectInitializationData.CreateSerializedInitializationData<BundledAssetProvider>());
            File.WriteAllText(Path.Combine(output, "catalog.json"), JsonUtility.ToJson(catalog));
            var release = new ResourceRelease
            {
                Id = id, Platform = "Windows", Architecture = "x64",
                ClientBuildId = "windows-aot-v1", FrameworkContractVersion = "1",
                ResourceCatalogId = id + "-catalog", CodeSetId = "builtin",
                SaveSchemaReadMin = 1, SaveSchemaReadMax = 1, SaveSchemaWriteVersion = 1,
                CatalogPath = "catalog.json",
                Files = new[] { Describe(output, "catalog.json"), Describe(output, bundle) }
            };
            File.WriteAllText(Path.Combine(output, "release.json"), JsonUtility.ToJson(release, true));
            Debug.Log("Windows 资源发布样例已生成: " + output);
        }

        static ReleaseFile Describe(string directory, string name)
        {
            using var stream = File.OpenRead(Path.Combine(directory, name));
            using var sha = SHA256.Create();
            return new ReleaseFile { Path = name, Length = stream.Length,
                Sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") };
        }
    }
}
