using NowFrameWork.Assets;
using NowFrameWork.UI;

namespace Game.FrameworkTemplate
{
    /// <summary>集中维护示例的 Addressables 地址和 UI 注册表，供编辑器生成器与运行时共同使用。</summary>
    public static class TemplateContent
    {
        public const string Prefix = "FrameworkTemplate/";
        public const string Home = Prefix + "UI/Home";
        public const string Modules = Prefix + "UI/Modules";
        public const string Settings = Prefix + "UI/Settings";
        public const string Toast = Prefix + "UI/Toast";
        public const string Config = Prefix + "Config";
        public const string Prefab = Prefix + "Prefab";
        public const string Scene = Prefix + "Scene";
        // 复用项目音频素材已有的 Addressables 地址。
        public const string Music = "audio/bgm";
        public const string Effect = "audio/sfx";

        // UiKey 限定 Open 或 Push 的参数类型，ID 与下方 UiDefinition 对应。
        public static readonly UiKey<TemplatePanel.Data> HomeKey = new UiKey<TemplatePanel.Data>(1, "Template Home");
        public static readonly UiKey<TemplatePanel.Data> ModulesKey = new UiKey<TemplatePanel.Data>(2, "Template Modules");
        public static readonly UiKey<TemplatePanel.Data> SettingsKey = new UiKey<TemplatePanel.Data>(3, "Template Settings");
        public static readonly UiKey<string> ToastKey = new UiKey<string>(4, "Template Toast");

        /// <summary>页面使用默认 Page 层。此处集中声明弹窗的遮罩、缓存和 Toast 的返回规则。</summary>
        public static UiDefinition[] Definitions() => new[]
        {
            new UiDefinition(HomeKey.Id, "Home", AssetKey.From(Home)),
            new UiDefinition(ModulesKey.Id, "ModuleLab", AssetKey.From(Modules)),
            new UiDefinition(SettingsKey.Id, "Settings", AssetKey.From(Settings))
            {
                Layer = UiLayer.Popup, Modal = true, CloseOnBackdrop = true, CacheOnClose = true
            },
            new UiDefinition(ToastKey.Id, "Toast", AssetKey.From(Toast))
            {
                Layer = UiLayer.Toast, BackBehavior = UiBackBehavior.Ignore
            }
        };
    }
}
