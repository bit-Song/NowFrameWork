using System.Linq;
using Game;
using NowFrameWork.Boot;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NowFrameWork.EditorTools.Tests
{
    public sealed class FrameworkPreflightTests
    {
        [TestCase("UIIntegration")]
        [TestCase("UIWindows")]
        [TestCase("UITween")]
        [TestCase("UILocalization")]
        [TestCase("BootIntegration")]
        public void ShippedSampleHasNoPreflightIssues(string name)
        {
            string path = "Assets/Scenes/" + name + ".scene";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var issues = FrameworkPreflight.InspectScene(scene, AddressableAssetSettingsDefaultObject.Settings);
                Assert.That(issues, Is.Empty, string.Join("\n", issues));
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, removeScene: true); }
        }

        [Test]
        public void BootReportsInvalidContractsAndConflictingOptions()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject host = NewHost(scene);
                var flow = host.AddComponent<BootFlow>();
                var events = host.AddComponent<EventSystem>();
                var releases = host.AddComponent<WindowsResourceReleases>();
                using var so = new SerializedObject(flow);
                so.FindProperty("_projectEntry").objectReferenceValue = events;
                so.FindProperty("_codeLoader").objectReferenceValue = events;
                so.FindProperty("_resourceReleases").objectReferenceValue = releases;
                so.FindProperty("_checkResourceUpdates").boolValue = true;
                so.FindProperty("_loadCode").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();

                var issues = FrameworkPreflight.InspectScene(scene, AddressableAssetSettingsDefaultObject.Settings);
                Assert.That(issues.Select(issue => issue.Code),
                    Is.EquivalentTo(new[] { "BOOT001", "BOOT002", "BOOT003", "ADDR001" }));
                Assert.That(issues.All(issue => issue.Context == host && issue.Location.Contains(host.name)));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void BootDistinguishesMissingConfigurationFromWrongSceneType()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var flow = NewHost(scene).AddComponent<BootFlow>();
                using var so = new SerializedObject(flow);
                so.FindProperty("_configAddress").stringValue = "Preflight/AbsentConfig";
                so.FindProperty("_sceneAddress").stringValue = "PCProbe/Config";
                so.ApplyModifiedPropertiesWithoutUndo();

                var issues = FrameworkPreflight.InspectScene(scene, AddressableAssetSettingsDefaultObject.Settings);
                Assert.That(issues.Select(issue => issue.Code), Is.EquivalentTo(new[] { "ADDR001", "ADDR002" }));
                Assert.That(issues.Single(issue => issue.Code == "ADDR002").Message, Does.Contain("SceneAsset"));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void UiReportsMissingRootAndPrefabWithoutView()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject host = NewHost(scene);
                var sample = host.AddComponent<UiFlowSample>();
                using var so = new SerializedObject(sample);
                so.FindProperty("_eventSystem").objectReferenceValue = host.AddComponent<EventSystem>();
                so.FindProperty("_panelAddress").stringValue = "PCProbe/Prefab";
                so.ApplyModifiedPropertiesWithoutUndo();

                var issues = FrameworkPreflight.InspectScene(scene, AddressableAssetSettingsDefaultObject.Settings);
                Assert.That(issues.Select(issue => issue.Code), Is.EquivalentTo(new[] { "UI001", "UI004" }));
                Assert.That(issues.Single(issue => issue.Code == "UI004").Message, Does.Contain("UiView"));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static GameObject NewHost(Scene scene)
        {
            var go = new GameObject("预检样例入口");
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }
    }
}
