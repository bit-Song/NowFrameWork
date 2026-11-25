using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace NowFrameWork.EditorTools
{
    /// <summary>把音频样例注册为 Addressable，避免手工拖拽遗漏。</summary>
    public static class AudioAssetSetup
    {
        static readonly (string Path, string Key)[] Assets =
        {
            ("Assets/Game/Audio/bgm.wav", "audio/bgm"),
            ("Assets/Game/Audio/sfx.wav", "audio/sfx"),
        };

        const string BootScene = "Assets/Scenes/BootIntegration.scene";

        /// <summary>注册音频资源、把探针挂到 BootIntegration、并构建 Addressables 内容。</summary>
        [MenuItem("NowFrameWork/Tests/一键配置音频验收")]
        public static void SetupAll()
        {
            Register();

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BootScene,
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            GameObject root = GameObject.Find("BootIntegration");
            if (root == null) throw new System.InvalidOperationException("场景里找不到 BootIntegration 根节点。");

            if (root.GetComponent<Game.AudioProbe>() == null) root.AddComponent<Game.AudioProbe>();

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            AddressableAssetSettings.BuildPlayerContent();
            Debug.Log("音频验收配置完成：Addressables 已构建，探针已加入 BootIntegration。");
        }

        [MenuItem("NowFrameWork/Tests/音频验收 1 切换到 Android")]
        public static void SwitchToAndroid()
        {
            if (!UnityEditor.EditorUserBuildSettings.SwitchActiveBuildTarget(
                    UnityEditor.BuildTargetGroup.Android, UnityEditor.BuildTarget.Android))
                throw new System.InvalidOperationException("切换 Android 平台失败。");

            Debug.Log("已切换到 Android 平台。");
        }

        [MenuItem("NowFrameWork/Tests/音频验收 2 构建 APK")]
        public static void BuildAndroidApk()
        {
            if (UnityEditor.EditorUserBuildSettings.activeBuildTarget != UnityEditor.BuildTarget.Android)
                throw new System.InvalidOperationException("请先切换到 Android 平台。");

            // 切平台后必须先重建 Addressables，否则包里仍是上一平台的 Bundle。
            AddressableAssetSettings.BuildPlayerContent();

            const string output = "Build/AudioProbe/NowFrameWork.apk";
            string directory = System.IO.Path.GetDirectoryName(output);
            if (!System.IO.Directory.Exists(directory)) System.IO.Directory.CreateDirectory(directory);

            var options = new UnityEditor.BuildPlayerOptions
            {
                scenes = new[] { BootScene },
                locationPathName = output,
                target = UnityEditor.BuildTarget.Android,
                options = UnityEditor.BuildOptions.Development,
            };

            UnityEditor.Build.Reporting.BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(options);

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.InvalidOperationException("APK 构建失败: " + report.summary.result);

            Debug.Log("APK 已生成: " + output);
        }

        [MenuItem("NowFrameWork/Tests/注册音频资源为 Addressable")]
        public static void Register()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new System.InvalidOperationException("项目没有 Addressables 设置。");

            AddressableAssetGroup group = settings.DefaultGroup;

            foreach ((string path, string key) in Assets)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) throw new System.InvalidOperationException("找不到资源: " + path);

                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = key;
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);
            AssetDatabase.SaveAssets();

            Debug.Log("音频资源已注册: audio/bgm, audio/sfx");
        }
    }
}
