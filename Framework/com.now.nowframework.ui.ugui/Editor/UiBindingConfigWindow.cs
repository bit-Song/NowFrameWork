using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NowFrameWork.UI.Editor
{
    /// <summary>UI 绑定生成器配置：输出目录、命名空间与字段风格。</summary>
    public sealed class UiBindingConfigWindow : EditorWindow
    {
        UiBindingOptions _options;
        UiBindingGenerator.Result _preview;
        Vector2 _scroll;

        [MenuItem("NowFrameWork/UI/Config", priority = 1)]
        public static void Open()
        {
            var window = GetWindow<UiBindingConfigWindow>("UI Binding");
            window.minSize = new Vector2(460f, 520f);
            window.Show();
        }

        [MenuItem("GameObject/NowFrameWork/UI/生成 UI 绑定脚本", false, 21)]
        public static void GenerateFromHierarchy()
        {
            Generate(Selection.activeGameObject);
        }

        [MenuItem("Assets/NowFrameWork/UI/生成 UI 绑定脚本", false, 21)]
        public static void GenerateFromProject()
        {
            Generate(Selection.activeGameObject);
        }

        [MenuItem("Assets/NowFrameWork/UI/生成 UI 绑定脚本", true)]
        public static bool ValidateGenerateFromProject()
        {
            return Selection.activeGameObject != null && AssetDatabase.Contains(Selection.activeGameObject);
        }

        [MenuItem("CONTEXT/UiView/生成 UI 绑定脚本", false, 20)]
        public static void GenerateFromComponent()
        {
            Generate(Selection.activeGameObject);
        }

        static void Generate(GameObject target)
        {
            if (target == null)
            {
                EditorUtility.DisplayDialog("UI 绑定", "请先在 Hierarchy 或 Project 中选中一个 UI 根节点。", "好");
                return;
            }

            UiBindingOptions options = UiBindingOptions.Load();

            if (!options.Folder.Replace("\\", "/").StartsWith("Assets/"))
            {
                EditorUtility.DisplayDialog("UI 绑定", "输出目录必须在项目的 Assets 目录下：" + options.Folder, "好");
                return;
            }

            UiBindingGenerator.Result result = UiBindingGenerator.Generate(target, options);

            if (result.ScriptPath != null)
                AssetDatabase.ImportAsset(result.ScriptPath);

            Debug.Log("[UI 绑定] " + (result.ScriptCreated ? "已创建 " : "已更新 ") + result.ScriptPath +
                "；字段 " + result.Fields.Count + "；绑定 " + (result.Bound ? "是" : "否"), target);

            foreach (string warning in result.Warnings) Debug.LogWarning("[UI 绑定] " + warning, target);

            if (result.Warnings.Count > 0)
                EditorUtility.DisplayDialog("UI 绑定完成（有提示）", string.Join("\n", result.Warnings), "好");

            if (result.ScriptPath != null)
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<MonoScript>(result.ScriptPath);
        }

        void OnEnable()
        {
            _options = UiBindingOptions.Load();
        }

        void OnGUI()
        {
            _options.Folder = EditorGUILayout.TextField("输出目录", _options.Folder);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("选择目录…", GUILayout.Width(120f)))
                {
                    string picked = EditorUtility.OpenFolderPanel("选择脚本输出目录", _options.Folder, string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        string dataPath = Application.dataPath.Replace("\\", "/");
                        _options.Folder = picked.Replace("\\", "/").StartsWith(dataPath)
                            ? "Assets" + picked.Substring(dataPath.Length)
                            : picked;
                    }
                }
            }

            _options.Namespace = EditorGUILayout.TextField("命名空间", _options.Namespace);
            _options.PublicFields = EditorGUILayout.Toggle("public 字段", _options.PublicFields);
            _options.LowerFirst = EditorGUILayout.Toggle("首字母小写", _options.LowerFirst);
            _options.UseTextMeshPro = EditorGUILayout.Toggle("识别 TextMeshPro", _options.UseTextMeshPro);
            _options.BindAfterGenerate = EditorGUILayout.Toggle("生成后写入引用", _options.BindAfterGenerate);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "命名规则：默认按组件类型推断（Button/Text/Image/Slider/Toggle/ScrollRect/InputField）。" +
                "需要强制类型时在节点名前加标记，例如 [Button]test1 → public Button test1;" +
                "支持 [Text] [Image] [Slider] [Toggle] [Scroll] [Input] [Rect] [TMP] [TMPInput]。",
                MessageType.Info);

            EditorGUILayout.Space();

            GameObject target = Selection.activeGameObject;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(target == null))
                {
                    if (GUILayout.Button("扫描选中（预览）")) Preview(target);
                    if (GUILayout.Button("生成")) Generate(target);
                }
            }

            if (target == null)
                EditorGUILayout.HelpBox("在 Hierarchy 或 Project 中选中 UI 根节点（面板 Prefab 或场景对象）。", MessageType.Warning);

            if (_preview != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("将生成 " + _preview.Fields.Count + " 个字段", EditorStyles.boldLabel);

                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(180f));

                foreach (UiBindingGenerator.Candidate field in _preview.Fields)
                    EditorGUILayout.LabelField(field.FieldName + "  :  " + field.TypeName + "   (" + field.Path + ")");

                EditorGUILayout.EndScrollView();

                foreach (string warning in _preview.Warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }

        void Preview(GameObject target)
        {
            UiBindingOptions options = UiBindingOptions.Load();
            _preview = UiBindingGenerator.Scan(target, options);
        }
    }
}
