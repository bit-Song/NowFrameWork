using System;
using UnityEditor;
using UnityEngine;

namespace NowFrameWork.UI.Editor
{
    /// <summary>UI 绑定脚本生成的选项，保存在 EditorPrefs 中。</summary>
    [Serializable]
    public sealed class UiBindingOptions
    {
        const string Prefix = "NowFrameWork.UI.Binding.v2.";

        /// <summary>生成脚本的输出目录。</summary>
        public string Folder = "Assets/NowFrameWork/UI/Generated";

        /// <summary>新脚本使用的命名空间。</summary>
        public string Namespace = "Game.UI";

        /// <summary>是否也推断没有标记的组件；关闭时只生成 [Button] 等标记节点。</summary>
        public bool InferUnmarked;

        /// <summary>字段使用 public 还是 [SerializeField] private。</summary>
        public bool PublicFields;

        /// <summary>字段名是否把首字母改为小写。</summary>
        public bool LowerFirst = true;

        /// <summary>是否识别 TextMeshPro 组件（需要目标程序集引用 TMP）。</summary>
        public bool UseTextMeshPro;

        /// <summary>是否为按钮生成 onClick 监听骨架。</summary>
        public bool GenerateEvents = true;

        /// <summary>是否生成 OnOpenAsync/OnCloseAsync 生命周期骨架。</summary>
        public bool GenerateLifecycle;

        /// <summary>是否按层级分组显示字段。</summary>
        public bool GroupByHierarchy = true;

        /// <summary>生成后是否立即写入引用。</summary>
        public bool BindAfterGenerate = true;

        public static UiBindingOptions Load()
        {
            return new UiBindingOptions
            {
                Folder = EditorPrefs.GetString(Prefix + "Folder", "Assets/NowFrameWork/UI/Generated"),
                Namespace = EditorPrefs.GetString(Prefix + "Namespace", "Game.UI"),
                InferUnmarked = EditorPrefs.GetBool(Prefix + "InferUnmarked", false),
                PublicFields = EditorPrefs.GetBool(Prefix + "PublicFields", false),
                LowerFirst = EditorPrefs.GetBool(Prefix + "LowerFirst", true),
                UseTextMeshPro = EditorPrefs.GetBool(Prefix + "UseTMP", false),
                GenerateEvents = EditorPrefs.GetBool(Prefix + "Events", true),
                GenerateLifecycle = EditorPrefs.GetBool(Prefix + "Lifecycle", false),
                GroupByHierarchy = EditorPrefs.GetBool(Prefix + "Groups", true),
                BindAfterGenerate = EditorPrefs.GetBool(Prefix + "Bind", true),
            };
        }

        public void Save()
        {
            EditorPrefs.SetString(Prefix + "Folder", Folder);
            EditorPrefs.SetString(Prefix + "Namespace", Namespace);
            EditorPrefs.SetBool(Prefix + "InferUnmarked", InferUnmarked);
            EditorPrefs.SetBool(Prefix + "PublicFields", PublicFields);
            EditorPrefs.SetBool(Prefix + "LowerFirst", LowerFirst);
            EditorPrefs.SetBool(Prefix + "UseTMP", UseTextMeshPro);
            EditorPrefs.SetBool(Prefix + "Events", GenerateEvents);
            EditorPrefs.SetBool(Prefix + "Lifecycle", GenerateLifecycle);
            EditorPrefs.SetBool(Prefix + "Groups", GroupByHierarchy);
            EditorPrefs.SetBool(Prefix + "Bind", BindAfterGenerate);
        }

        public UiBindingOptions Clone() => (UiBindingOptions)MemberwiseClone();

        /// <summary>字段声明文本。</summary>
        public string FieldDeclaration(string type, string name)
            => PublicFields
                ? "public " + type + " " + name + ";"
                : "[SerializeField] private " + type + " " + name + ";";
    }
}
