using System;
using System.IO;
using Game;
using Game.FrameworkTemplate;
using Game.Localization;
using NowFrameWork.Boot;
using NowFrameWork.UI;
using NowFrameWork.UI.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NowFrameWork.EditorTools
{
    /// <summary>编辑器端搭建示例的场景、Prefab 和 Addressables 登记。运行时直接使用这些资产。</summary>
    public static class FrameworkTemplateMenu
    {
        const string Folder = "Assets/Game/FrameworkTemplate/Content";
        public const string ScenePath = "Assets/Scenes/FrameworkTemplate.scene";
        const string Fixture = Folder + "/FrameworkTemplateFixture.scene";
        const string Root = Folder + "/UiRoot.prefab";

        static readonly Color Panel = new Color32(21, 32, 48, 255);
        static readonly Color Ink = new Color32(235, 242, 251, 255);
        static readonly Color Muted = new Color32(173, 194, 214, 255);
        static readonly Color Accent = new Color32(117, 227, 207, 255);

        [MenuItem("NowFrameWork/Examples/Open Framework Template")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>从空 Content 目录生成示例。已存在的场景和 Prefab 会保留，资源地址会按模板登记。</summary>
        public static void Create()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("Addressables settings are missing.");

            Directory.CreateDirectory(Folder);
            var root = UiPrefabBuilder.CreateRoot(Root);
            CreatePage("Home", "Framework template", "Boot · UI · Addressables", false,
                "Module lab", "Settings", "Show toast");
            CreatePage("Modules", "Module lab", "Use the actions below to try each module.", false,
                "Read config", "Rent / return cube", "Load / unload scene", "Event + timer", "Advance HFSM",
                "Dump resource leases", "Hide / show page", "Trim UI cache", "Back");
            CreatePage("Settings", "Settings", "Modal popup · click outside or press Esc to close", true,
                "English", "简体中文", "Start / stop BGM", "Play two effects", "Mute / restore volume",
                "Switch Pulse key P / O", "Check conflict with I", "Close");
            CreateToast();
            CreateWorldAssets();
            string config = Folder + "/ExampleConfig.txt";
            if (!File.Exists(config)) File.WriteAllText(config, "Framework template configuration (v1)\n");
            AssetDatabase.Refresh();

            // UiRoot 是场景的直接引用。面板、配置、立方体和附加场景登记在本地 Addressables 分组。
            var group = settings.FindGroup("Framework Template Local") ??
                settings.CreateGroup("Framework Template Local", false, false, false, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(settings, "Local.BuildPath");
            schema.LoadPath.SetVariableByName(settings, "Local.LoadPath");
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            EditorUtility.SetDirty(schema);
            Register(settings, group, Folder + "/Home.prefab", TemplateContent.Home);
            Register(settings, group, Folder + "/Modules.prefab", TemplateContent.Modules);
            Register(settings, group, Folder + "/Settings.prefab", TemplateContent.Settings);
            Register(settings, group, Folder + "/Toast.prefab", TemplateContent.Toast);
            Register(settings, group, Folder + "/Cube.prefab", TemplateContent.Prefab);
            Register(settings, group, config, TemplateContent.Config);
            Register(settings, group, Fixture, TemplateContent.Scene);
            if (!File.Exists(ScenePath)) CreateBootScene(root);
            AssetDatabase.SaveAssets();
        }

        static void Register(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
        {
            // 编辑器用资产 GUID 找到记录，运行时通过 TemplateContent 中的地址加载。
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) throw new FileNotFoundException(path);
            settings.CreateOrMoveEntry(guid, group).SetAddress(address);
        }

        static void CreateBootScene(UiRoot root)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("Framework Template", typeof(InputSystemDeviceWatcher), typeof(FrameworkTemplateEntry), typeof(BootFlow));
            var camera = new GameObject("Demo Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.transform.SetParent(host.transform, false);
            camera.transform.localPosition = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(12, 19, 31, 255);
            // 全场景共用一个 EventSystem，原生输入模块驱动 UI 移动、点击和确认。
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<EventSystem>();
            events.transform.SetParent(host.transform, false);
            var entry = host.GetComponent<FrameworkTemplateEntry>();
            entry.Configure(root, events, host.GetComponent<InputSystemDeviceWatcher>());
            // 将项目入口写入 BootFlow 的序列化字段。启动成功后 Boot 会调用该入口的 EnterAsync。
            var serialized = new SerializedObject(host.GetComponent<BootFlow>());
            serialized.FindProperty("_projectEntry").objectReferenceValue = entry;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SaveScene(scene, ScenePath);
        }

        static void CreatePage(string name, string title, string subtitle, bool modal, params string[] labels)
        {
            string path = Folder + "/" + name + ".prefab";
            if (File.Exists(path)) return;
            var root = PanelRoot<TemplatePanel>(name, raycaster: true);
            try
            {
                // 面板根节点由 UiModule 放进对应层。面板内容单独使用安全区，框架生成的遮罩仍可覆盖视口。
                RectTransform safe = Rect("SafeArea", root.transform, Vector2.zero, Vector2.one);
                safe.gameObject.AddComponent<UiSafeArea>();
                RectTransform body = modal
                    ? Rect("Dialog", safe, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                        new Vector2(-625, -354), new Vector2(625, 354))
                    : Rect("Layout", safe, new Vector2(.09f, .07f), new Vector2(.93f, .82f));
                if (modal) Paint(body, Panel, raycast: true);
                RectTransform header = Rect("Header", body, new Vector2(0, 1), Vector2.one,
                    new Vector2(modal ? 36 : 0, -135), new Vector2(-30, -20));
                Label(Rect("Title", header, new Vector2(0, .4f), Vector2.one), title, modal ? 42 : 58, Ink, TextAnchor.MiddleLeft);
                Label(Rect("Description", header, Vector2.zero, new Vector2(1, .4f)), subtitle, 24, Accent, TextAnchor.MiddleLeft);
                var actions = Rect("Actions", body, Vector2.zero, new Vector2(.47f, 1),
                    new Vector2(modal ? 36 : 0, 24), new Vector2(-24, -158));
                var layout = actions.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 9;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                // 生成顺序同时决定布局、焦点导航和命令索引，应与入口传入的命令顺序对应。
                Button[] buttons = new Button[labels.Length];
                for (int i = 0; i < labels.Length; i++) buttons[i] = MakeButton(actions, labels[i]);

                var card = Rect("Status Card", body, new Vector2(.49f, modal ? 0 : .43f), Vector2.one,
                    new Vector2(0, modal ? 36 : 0), new Vector2(modal ? -36 : 0, -158));
                Paint(card, modal ? new Color32(31, 46, 66, 255) : Panel, raycast: false);
                Label(Rect("Caption", card, new Vector2(0, 1), Vector2.one, new Vector2(25, -53), new Vector2(-25, -16)),
                    "LIVE STATUS", 23, Accent, TextAnchor.MiddleLeft);
                Text status = Label(Rect("Status", card, modal ? new Vector2(0, .29f) : Vector2.zero, Vector2.one,
                    new Vector2(25, 18), new Vector2(-25, -67)), "", 24, Ink, TextAnchor.UpperLeft);
                if (modal)
                {
                    var translation = Label(Rect("Localized Text", card, Vector2.zero, new Vector2(1, .29f),
                        new Vector2(25, 12), new Vector2(-25, 0)), "", 24, Muted, TextAnchor.UpperLeft);
                    translation.gameObject.AddComponent<LocalizedSampleText>();
                }
                root.GetComponent<TemplatePanel>().Configure(buttons, status);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Button MakeButton(Transform parent, string title)
        {
            var rect = Rect(title, parent, Vector2.zero, Vector2.one);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 51;
            Image image = Paint(rect, new Color32(34, 52, 74, 255), raycast: true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.selectedColor = new Color32(91, 191, 176, 255);
            colors.highlightedColor = new Color32(140, 205, 201, 255);
            colors.pressedColor = new Color32(98, 163, 169, 255);
            button.colors = colors;
            Label(Rect("Label", rect, Vector2.zero, Vector2.one, new Vector2(18, 0), new Vector2(-14, 0)),
                title, 25, Ink, TextAnchor.MiddleLeft);
            return button;
        }

        static void CreateToast()
        {
            string path = Folder + "/Toast.prefab";
            if (File.Exists(path)) return;
            // Toast 的视觉元素都关闭射线检测，显示时下层按钮仍可以接收指针输入。
            var root = PanelRoot<TemplateToast>("Toast", raycaster: false);
            try
            {
                var chip = Rect("Message", root.transform, new Vector2(.5f, .08f), new Vector2(.5f, .08f),
                    new Vector2(-335, 0), new Vector2(335, 70));
                Paint(chip, new Color32(28, 68, 70, 255), raycast: false);
                var text = Label(Rect("Label", chip, Vector2.zero, Vector2.one), "", 25, Ink, TextAnchor.MiddleCenter);
                root.GetComponent<TemplateToast>().Configure(text);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static GameObject PanelRoot<T>(string name, bool raycaster) where T : UiView
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(T));
            if (raycaster) root.AddComponent<GraphicRaycaster>();
            return root;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 lower = default, Vector2 upper = default)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = lower;
            rect.offsetMax = upper;
            return rect;
        }

        static Image Paint(RectTransform rect, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static Text Label(RectTransform rect, string value, int size, Color color, TextAnchor alignment)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        static void CreateWorldAssets()
        {
            string cubePath = Folder + "/Cube.prefab";
            if (!File.Exists(cubePath))
            {
                var cube = Primitive("Cube", PrimitiveType.Cube, new Vector3(2.1f, -1.8f, 0), new Color32(76, 209, 184, 255));
                try
                {
                    cube.transform.rotation = Quaternion.Euler(30, 35, 15);
                    PrefabUtility.SaveAsPrefabAsset(cube, cubePath);
                }
                finally { Object.DestroyImmediate(cube); }
            }
            if (File.Exists(Fixture)) return;
            // 球保存到独立场景，运行时通过 Addressables 附加到启动场景。
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Primitive("Additive Sphere", PrimitiveType.Sphere, new Vector3(4.7f, -1.8f, 0), new Color32(235, 173, 102, 255));
            SaveScene(scene, Fixture);
        }

        static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Color color)
        {
            string path = Folder + "/" + type + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("URP Unlit shader is missing.");
                material = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(material, path);
            }
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.position = position;
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(primitive.GetComponent<Collider>());
            return primitive;
        }

        static void SaveScene(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save " + path);
        }
    }
}
