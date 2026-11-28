using System;
using UnityEditor;
using UnityEngine;

namespace NowFrameWork.UI.Editor
{
    /// <summary>UI 绑定脚本生成的选项，保存在 EditorPrefs 中。</summary>
    [Serializable]
    public sealed class UiBindingOptions
    {
        const string Prefix = "NowFrameWork.UI.Binding.";

        /// <summary>生成脚本的输出目录。</summary>
        public string Folder = "Assets/Game/UI/Generated";

        /// <summary>新脚本使用的命名空间。</summary>
        public string Namespace = "Game.UI";

        /// <summary>字段使用 public 还是 [SerializeField] private。</summary>
        public bool PublicFields;

        /// <summary>字段名是否把首字母改为小写。</summary>
        public bool LowerFirst = true;

        /// <summary>是否识别 TextMeshPro 组件（需要目标程序集引用 TMP）。</summary>
        public bool UseTextMeshPro;

        /// <summary>生成后是否立即写入引用。</summary>
        public bool BindAfterGenerate = true;

        public static UiBindingOptions Load()
        {
            return new UiBindingOptions
            {
                Folder = EditorPrefs.GetString(Prefix + "Folder", "Assets/Game/UI/Generated"),
                Namespace = EditorPrefs.GetString(Prefix + "Namespace", "Game.UI"),
                PublicFields = EditorPrefs.GetBool(Prefix + "PublicFields", false),
                LowerFirst = EditorPrefs.GetBool(Prefix + "LowerFirst", true),
                UseTextMeshPro = EditorPrefs.GetBool(Prefix + "UseTMP", false),
                BindAfterGenerate = EditorPrefs.GetBool(Prefix + "Bind", true),
            };
        }

        public void Save()
        {
            EditorPrefs.SetString(Prefix + "Folder", Folder);
            EditorPrefs.SetString(Prefix + "Namespace", Namespace);
            EditorPrefs.SetBool(Prefix + "PublicFields", PublicFields);
            EditorPrefs.SetBool(Prefix + "LowerFirst", LowerFirst);
            EditorPrefs.SetBool(Prefix + "UseTMP", UseTextMeshPro);
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
