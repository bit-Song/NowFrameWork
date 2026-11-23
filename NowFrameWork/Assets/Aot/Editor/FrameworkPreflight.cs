using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using NowFrameWork.Boot;
using NowFrameWork.UI;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NowFrameWork.EditorTools
{
    /// <summary>编辑器装配预检。只读取场景和项目 Addressables 设置。</summary>
    public static class FrameworkPreflight
    {
        const string ActiveSceneMenu = "NowFrameWork/检查/预检当前场景";
        const string BuildScenesMenu = "NowFrameWork/检查/预检启用的构建场景";

        public sealed class Issue
        {
            public string Code { get; }
            public string Location { get; }
            public string Message { get; }
            public Object Context { get; }

            internal Issue(string code, string location, string message, Object context)
            {
                Code = code;
                Location = location;
                Message = message;
                Context = context;
            }

            public override string ToString() => "[" + Code + "] " + Location + " — " + Message;
        }

        [MenuItem(ActiveSceneMenu, true)]
        [MenuItem(BuildScenesMenu, true)]
        static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        [MenuItem(ActiveSceneMenu)]
        public static void CheckActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Print(InspectScene(scene, AddressableAssetSettingsDefaultObject.Settings), "当前场景 " + scene.name);
        }

        [MenuItem(BuildScenesMenu)]
        public static void CheckEnabledBuildScenes()
        {
            var issues = InspectEnabledBuildScenes(out int count);
            Print(issues, "启用的构建场景 " + count + " 个");
        }

        internal static IReadOnlyList<Issue> InspectEnabledBuildScenes(out int count)
        {
            var issues = new List<Issue>();
            var catalog = new CatalogIndex(AddressableAssetSettingsDefaultObject.Settings);
            count = 0;
            foreach (var entry in EditorBuildSettings.scenes.Where(scene => scene.enabled))
            {
                count++;
                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.path);
                if (asset == null)
                {
                    issues.Add(new Issue("SCENE001", entry.path, "启用的构建场景不存在", null));
                    continue;
                }

                Scene loaded = SceneManager.GetSceneByPath(entry.path);
                if (loaded.IsValid() && loaded.isLoaded)
                {
                    Collect(loaded, catalog, issues);
                    continue;
                }

                Scene active = SceneManager.GetActiveScene();
                Scene opened = default;
                try
                {
                    opened = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
                    Collect(opened, catalog, issues, asset);
                }
                catch (Exception error)
                {
                    issues.Add(new Issue("SCENE002", entry.path, "无法预检场景: " + error.Message, asset));
                }
                finally
                {
                    if (opened.IsValid()) EditorSceneManager.CloseScene(opened, removeScene: true);
                    if (active.IsValid() && active.isLoaded && SceneManager.GetActiveScene() != active)
                        SceneManager.SetActiveScene(active);
                }
            }
            return issues;
        }

        public static IReadOnlyList<Issue> InspectScene(Scene scene, AddressableAssetSettings settings)
        {
            var issues = new List<Issue>();
            Collect(scene, new CatalogIndex(settings), issues);
            return issues;
        }

        static void Collect(Scene scene, CatalogIndex catalog, List<Issue> issues, SceneAsset sceneAsset = null)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                issues.Add(new Issue("SCENE002", scene.path, "场景尚未加载", sceneAsset));
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = node.gameObject;
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                    if (missing != 0)
                        Report(issues, go, sceneAsset, "SCENE003", "有 " + missing + " 个脚本组件丢失");
                }
                foreach (var flow in root.GetComponentsInChildren<BootFlow>(true))
                    CheckBoot(flow, catalog, issues, sceneAsset);
                foreach (var ui in root.GetComponentsInChildren<UiFlowSample>(true))
                    CheckUi(ui, catalog, issues, sceneAsset, "_panelAddress");
                foreach (var ui in root.GetComponentsInChildren<UiWindowsSample>(true))
                    CheckUi(ui, catalog, issues, sceneAsset, "_homePrefab", "_inventoryPrefab", "_settingsPrefab", "_confirmPrefab");
            }
        }

        static void CheckBoot(BootFlow flow, CatalogIndex catalog, List<Issue> issues, SceneAsset sceneAsset)
        {
            using var so = new SerializedObject(flow);
            void Add(string code, string message) => Report(issues, flow.gameObject, sceneAsset, code, message);

            if (!catalog.HasSettings) Add("ADDR000", "BootFlow 需要 Addressables Settings 资产");
            var entry = so.FindProperty("_projectEntry");
            if (entry.objectReferenceValue != null && !(entry.objectReferenceValue is IBootEntry))
                Add("BOOT001", "Project Entry 必须实现 IBootEntry");
            else if (entry.objectReferenceValue == null && (entry.objectReferenceInstanceIDValue != 0 ||
                flow.GetComponents<MonoBehaviour>().Any(component => component is IBootEntry)))
                Add("BOOT001", "Project Entry 引用缺失或未绑定当前对象上的入口组件");

            bool updates = so.FindProperty("_checkResourceUpdates").boolValue;
            bool loadCode = so.FindProperty("_loadCode").boolValue;
            var releases = so.FindProperty("_resourceReleases");
            if (releases.objectReferenceValue == null && releases.objectReferenceInstanceIDValue != 0)
                Add("BOOT002", "Resource Releases 引用缺失");
            if (releases.objectReferenceValue != null)
            {
                if (updates || loadCode)
                    Add("BOOT002", "资源发布模式要求同时关闭 Check Resource Updates 和 Load Code");
                if (catalog.AllowsAutomaticCatalogUpdate)
                    Add("BOOT004", "资源发布模式要求关闭 Addressables 启动时自动更新 Catalog");
            }
            if (loadCode && !(so.FindProperty("_codeLoader").objectReferenceValue is IBootCodeLoader))
                Add("BOOT003", "开启 Load Code 时，Code Loader 必须实现 IBootCodeLoader");

            string config = so.FindProperty("_configAddress").stringValue;
            if (!string.IsNullOrEmpty(config))
                CheckAsset(catalog, config, "Config Address", typeof(Object), Add);
            string scene = so.FindProperty("_sceneAddress").stringValue;
            if (!string.IsNullOrEmpty(scene))
                CheckAsset(catalog, scene, "Scene Address", typeof(SceneAsset), Add);
            if (updates)
            {
                string updateKey = so.FindProperty("_updateKey").stringValue;
                if (string.IsNullOrWhiteSpace(updateKey) || catalog.Find(updateKey).Count == 0)
                    Add("ADDR001", "Update Key 须匹配已参与构建的 Addressables 地址、GUID 或标签: " + updateKey);
            }
        }

        static void CheckUi(MonoBehaviour sample, CatalogIndex catalog, List<Issue> issues, SceneAsset sceneAsset,
            params string[] fields)
        {
            using var so = new SerializedObject(sample);
            void Add(string code, string message) => Report(issues, sample.gameObject, sceneAsset, code, message);
            var root = so.FindProperty("_rootPrefab").objectReferenceValue as UiRoot;
            if (root == null || !PrefabUtility.IsPartOfPrefabAsset(root))
                Add("UI001", "Root Prefab 必须引用 Prefab 资产上的 UiRoot");
            else
            {
                using var rootSettings = new SerializedObject(root);
                var safeArea = rootSettings.FindProperty("_safeArea").objectReferenceValue as Transform;
                if (root.Canvas == null || root.Canvas.transform != root.transform || safeArea == null ||
                    safeArea == root.transform || !safeArea.IsChildOf(root.transform))
                    Add("UI002", "UiRoot 缺少自身 Canvas 或有效的安全区容器");
                else if (root.Canvas.renderMode == RenderMode.WorldSpace ||
                    (root.Canvas.renderMode == RenderMode.ScreenSpaceCamera && root.Canvas.worldCamera == null) ||
                    root.Canvas.sortingOrder < -32768 || root.Canvas.sortingOrder > 8191)
                    Add("UI002", "UiRoot 的 Canvas 模式、相机或排序超出运行时支持范围");

                try
                {
                    var page = root.GetLayer(UiLayer.Page);
                    var popup = root.GetLayer(UiLayer.Popup);
                    var toast = root.GetLayer(UiLayer.Toast);
                    if (page == popup || page == toast || popup == toast)
                        Add("UI002", "UiRoot 的页面、弹窗和提示层必须引用不同容器");
                }
                catch (InvalidOperationException error) { Add("UI002", error.Message); }
            }

            var reference = so.FindProperty("_eventSystem");
            var events = reference.objectReferenceValue as EventSystem;
            if (events == null && reference.objectReferenceInstanceIDValue == 0 && root != null)
                events = root.EventSystem;
            if (events == null || !events.enabled || (!EditorUtility.IsPersistent(events) && !events.gameObject.activeInHierarchy))
                Add("UI003", "需要启用的 EventSystem，来自场景或 UiRoot Prefab");
            else if (events.GetComponents<BaseInputModule>().Count(module => module.enabled) > 1)
                Add("UI003", "同一 EventSystem 启用了多套输入模块");

            foreach (string field in fields)
            {
                string key = so.FindProperty(field).stringValue;
                string path = CheckAsset(catalog, key, field, typeof(GameObject), Add);
                if (path == null) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab) || prefab.GetComponent<UiView>() == null)
                    Add("UI004", field + " 指向的 Prefab 根缺少 UiView: " + path);
            }
        }

        static string CheckAsset(CatalogIndex catalog, string key, string field, Type type, Action<string, string> add)
        {
            var paths = catalog.Find(key);
            if (paths.Count == 0)
            {
                add("ADDR001", field + " 找不到 Addressables 资源: " + key);
                return null;
            }

            var matches = paths.Where(path =>
            {
                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                return assetType != null && (type == typeof(Object)
                    ? assetType != typeof(SceneAsset) : type.IsAssignableFrom(assetType));
            }).ToArray();
            if (matches.Length == 1) return matches[0];

            add(matches.Length == 0 ? "ADDR002" : "ADDR003", field +
                (matches.Length == 0 ? " 找不到所需类型 " : " 匹配多个同类型资源 ") +
                type.Name + ": " + key + " (" + string.Join(", ", paths) + ")");
            return null;
        }

        static void Report(List<Issue> issues, GameObject go, SceneAsset sceneAsset, string code, string message)
        {
            string scene = string.IsNullOrEmpty(go.scene.path) ? go.scene.name : go.scene.path;
            string hierarchy = AnimationUtility.CalculateTransformPath(go.transform, null);
            issues.Add(new Issue(code, scene + " / " + hierarchy, message, sceneAsset != null ? (Object)sceneAsset : go));
        }

        internal static void Print(IReadOnlyList<Issue> issues, string scope)
        {
            foreach (Issue issue in issues) Debug.LogError(issue.ToString(), issue.Context);
            Debug.Log("[预检] " + scope + "，发现 " + issues.Count + " 项问题");
        }

        sealed class CatalogIndex
        {
            readonly Dictionary<string, HashSet<string>> _paths = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            public bool HasSettings { get; }
            public bool AllowsAutomaticCatalogUpdate { get; }

            public CatalogIndex(AddressableAssetSettings settings)
            {
                if (settings == null) return;
                HasSettings = true;
                AllowsAutomaticCatalogUpdate = !settings.DisableCatalogUpdateOnStartup;
                var entries = new List<AddressableAssetEntry>();
                settings.GetAllAssets(entries, includeSubObjects: false,
                    groupFilter: group => group.GetSchema<BundledAssetGroupSchema>()?.IncludeInBuild != false);
                foreach (var entry in entries)
                {
                    string path = entry.AssetPath;
                    if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path) ||
                        AssetDatabase.GetMainAssetTypeAtPath(path) == null) continue;
                    var schema = entry.parentGroup?.GetSchema<BundledAssetGroupSchema>();
                    if (schema?.IncludeAddressInCatalog != false) Add(entry.address, path);
                    if (schema?.IncludeGUIDInCatalog != false) Add(entry.guid, path);
                    if (schema?.IncludeLabelsInCatalog != false)
                        foreach (string label in entry.labels) Add(label, path);
                }
            }

            public IReadOnlyCollection<string> Find(string key)
            {
                if (!string.IsNullOrWhiteSpace(key) && _paths.TryGetValue(key, out var paths)) return paths;
                return Array.Empty<string>();
            }

            void Add(string key, string path)
            {
                if (string.IsNullOrEmpty(key)) return;
                if (!_paths.TryGetValue(key, out var paths)) _paths.Add(key, paths = new HashSet<string>());
                paths.Add(path);
            }
        }
    }
}
