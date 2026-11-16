using System;
using System.Collections.Generic;
using System.IO;
using HybridCLR;
using HybridCLR.Commands;
using NowFrameWork.AssetTest;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace NowFrameWork.EditorTools
{
    /// <summary>可重复的 PC 回归夹具与构建入口；生成产物留在 Build/PCRegression。</summary>
    public static class PcRegressionBuild
    {
        const string MainScene = "Assets/Scenes/AssetTest.scene";
        const string BuildRoot = "Build/PCRegression";
        const string StateFile = BuildRoot + "/baseline-content-state.bin";

        [MenuItem("NowFrameWork/Tests/PC/1 生成回归夹具")]
        public static void CreateFixtures()
        {
            Directory.CreateDirectory(PcProbeAssets.Root);
            AssetDatabase.Refresh();
            CreatePrefab();
            File.WriteAllText(PcProbeAssets.Root + "/Config.txt", "pc-regression-config-v1");
            byte[] bytes = new byte[256 * 1024];
            new System.Random(18182).NextBytes(bytes);
            File.WriteAllBytes(PcProbeAssets.Root + "/Remote.bytes", bytes);
            CreateScene(PcProbeAssets.Root + "/PCRegressionFixture.scene", "PCRegressionFixture");
            CreateScene(PcProbeAssets.Root + "/PCRegressionRemote.scene", "PCRegressionRemote");
            CreateAtlas();
            if (EditorSettings.spritePackerMode == SpritePackerMode.Disabled)
                EditorSettings.spritePackerMode = SpritePackerMode.BuildTimeOnlyAtlas;
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(PcProbeAssets.Root + "/ProbeAtlas.spriteatlas");
            SpriteAtlasUtility.PackAtlases(new[] { atlas }, BuildTarget.StandaloneWindows64);
            if (atlas.spriteCount != 2) throw new InvalidOperationException("回归图集未打包两个 Sprite");
            AssetDatabase.Refresh();
            ConfigureAddressables();
            AssetTestSceneMenu.CreateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[PCBuild] Fixtures ready");
        }

        static void CreatePrefab()
        {
            string path = PcProbeAssets.Root + "/Probe.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            var go = new GameObject("PCRegressionPrefab");
            PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
        }

        static void CreateScene(string path, string marker)
        {
            if (File.Exists(path)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject(marker);
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("保存场景失败: " + path);
        }

        static void CreateAtlas()
        {
            string atlasPath = PcProbeAssets.Root + "/ProbeAtlas.spriteatlas";
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas != null) return;
            var sprites = new List<UnityEngine.Object>();
            foreach (string name in new[] { "ProbeRed", "ProbeBlue" })
            {
                string path = PcProbeAssets.Root + "/" + name + ".png";
                var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                Color color = name == "ProbeRed" ? Color.red : Color.blue;
                var colors = new Color[256];
                for (int i = 0; i < colors.Length; i++) colors[i] = color;
                texture.SetPixels(colors);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                sprites.Add(AssetDatabase.LoadAssetAtPath<Sprite>(path));
            }
            atlas = new SpriteAtlas();
            atlas.Add(sprites.ToArray());
            var settings = atlas.GetPackingSettings();
            settings.enableRotation = false;
            atlas.SetPackingSettings(settings);
            atlas.SetIncludeInBuild(false);
            AssetDatabase.CreateAsset(atlas, atlasPath);
        }

        static void ConfigureAddressables()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("请先创建 Addressables Settings");
            string profile = settings.profileSettings.GetProfileId("PCRegression");
            if (string.IsNullOrEmpty(profile)) profile = settings.profileSettings.AddProfile("PCRegression", settings.activeProfileId);
            settings.profileSettings.SetValue(profile, "Remote.BuildPath", BuildRoot + "/ServerData/[BuildTarget]");
            settings.profileSettings.SetValue(profile, "Remote.LoadPath", "http://127.0.0.1:18182/[BuildTarget]");
            settings.activeProfileId = profile;
            settings.BuildRemoteCatalog = true;
            settings.DisableCatalogUpdateOnStartup = true;
            settings.CatalogRequestsTimeout = 3;
            settings.OverridePlayerVersion = "pc-regression-v1";
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, "Remote.BuildPath");
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, "Remote.LoadPath");
            var local = Group(settings, "PCRegression Local", false);
            Entry(settings, local, PcProbeAssets.Root + "/Probe.prefab", PcProbeAssets.Prefab);
            Entry(settings, local, PcProbeAssets.Root + "/Config.txt", PcProbeAssets.Config);
            Entry(settings, local, PcProbeAssets.Root + "/PCRegressionFixture.scene", PcProbeAssets.Scene);
            Entry(settings, local, PcProbeAssets.Root + "/ProbeAtlas.spriteatlas", PcProbeAssets.Atlas);
            var remote = Group(settings, "PCRegression Remote", true);
            Entry(settings, remote, PcProbeAssets.Root + "/Remote.bytes", PcProbeAssets.RemoteData);
            Entry(settings, remote, PcProbeAssets.Root + "/PCRegressionRemote.scene", PcProbeAssets.RemoteScene);
            Entry(settings, remote, "Assets/HotUpdateDlls/HotUpdate.dll.bytes", PcProbeAssets.Dll);
            EditorUtility.SetDirty(settings);
        }

        static AddressableAssetGroup Group(AddressableAssetSettings settings, string name, bool remote)
        {
            var group = settings.FindGroup(name) ?? settings.CreateGroup(name, false, false, false, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(settings, remote ? "Remote.BuildPath" : "Local.BuildPath");
            schema.LoadPath.SetVariableByName(settings, remote ? "Remote.LoadPath" : "Local.LoadPath");
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            schema.Timeout = 10;
            schema.RetryCount = 0;
            EditorUtility.SetDirty(schema);
            return group;
        }

        static void Entry(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) throw new FileNotFoundException(path);
            settings.CreateOrMoveEntry(guid, group).address = address;
        }

        [MenuItem("NowFrameWork/Tests/PC/2 准备 V1 资源和 HybridCLR")]
        public static void PrepareBaseline()
        {
            EnsureWindows();
            CreateFixtures();
            EditorUserBuildSettings.development = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PrebuildCommand.GenerateAll();
            BuildBaselineResources();
        }

        [MenuItem("NowFrameWork/Tests/PC/仅重建 V1 夹具资源")]
        public static void RebuildBaselineResources()
        {
            EnsureWindows();
            CreateFixtures();
            BuildBaselineResources();
        }

        static void BuildBaselineResources()
        {
            CompileHotDll(false);
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException(result.Error);
            string state = ContentUpdateScript.GetContentStateDataPath(false);
            Directory.CreateDirectory(BuildRoot);
            File.Copy(state, StateFile, true);
            Snapshot("V1");
            Debug.Log("[PCBuild] Baseline resources ready; build Player in a fresh editor process");
        }

        [MenuItem("NowFrameWork/Tests/PC/3 构建 Windows IL2CPP Player")]
        public static void BuildWindows()
        {
            EnsureWindows();
            if (!File.Exists(StateFile)) throw new FileNotFoundException("先准备 V1 资源", StateFile);
            string previousProduct = PlayerSettings.productName;
            try
            {
                PlayerSettings.productName = "NowFrameWork-PCRegression";
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { MainScene },
                    locationPathName = BuildRoot + "/Player/PCRegression.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("PC Player 构建失败: " + report.summary.result);
                Debug.Log("[PCBuild] Player ready: " + options.locationPathName);
            }
            finally { PlayerSettings.productName = previousProduct; AssetDatabase.SaveAssets(); }
        }

        [MenuItem("NowFrameWork/Tests/PC/4 仅发布 V2 DLL 补丁")]
        public static void PublishPatch()
        {
            EnsureWindows();
            if (!File.Exists(StateFile)) throw new FileNotFoundException("缺少 V1 内容状态文件", StateFile);
            CompileHotDll(true);
            AddressablesPlayerBuildResult result = ContentUpdateScript.BuildContentUpdate(AddressableAssetSettingsDefaultObject.Settings, StateFile);
            if (result == null || !string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException("补丁构建失败: " + result?.Error);
            Snapshot("V2");
            Debug.Log("[PCBuild] V2 published; Player was NOT rebuilt");
        }

        static void CompileHotDll(bool patch)
        {
            string output = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64);
            Directory.CreateDirectory(output);
            var compile = new ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = ScriptCompilationOptions.DevelopmentBuild,
                extraScriptingDefines = patch ? new[] { "PC_PROBE_PATCH_V2" } : Array.Empty<string>()
            };
            PlayerBuildInterface.CompilePlayerScripts(compile, output);
            File.Copy(Path.Combine(output, "HotUpdate.dll"), "Assets/HotUpdateDlls/HotUpdate.dll.bytes", true);
            AssetDatabase.ImportAsset("Assets/HotUpdateDlls/HotUpdate.dll.bytes", ImportAssetOptions.ForceSynchronousImport);
        }

        static void EnsureWindows()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("请切换 Windows x64，或命令行指定 -buildTarget Win64");
        }

        static void Snapshot(string release)
        {
            string source = Path.GetFullPath(BuildRoot + "/ServerData");
            string destination = Path.GetFullPath(BuildRoot + "/Releases/" + release);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, file.Substring(source.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }
    }
}
