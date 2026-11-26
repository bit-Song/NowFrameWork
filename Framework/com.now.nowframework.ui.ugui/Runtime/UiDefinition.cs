using NowFrameWork.Assets;

namespace NowFrameWork.UI
{
    public enum UiLayer
    {
        Page = 0,
        Popup = 1,
        Toast = 2,
    }

    /// <summary>
    /// 界面定义：注册表在模块启动前建立并冻结。
    ///
    /// 一个定义对应**一个** addressable Prefab；图集/字体等依赖由 Addressables 自动带上，
    /// bundle 如何划分由项目在 Group 里配置，这里不感知。
    /// </summary>
    public sealed class UiDefinition
    {
        /// <summary>声明一个面板：ID、名称与 Prefab 地址。</summary>
        public UiDefinition(int id, string name, AssetKey prefab)
        {
            Id = id;
            Name = name;
            Prefab = prefab;
        }

        /// <summary>面板 ID，需与 UiKey 一致。</summary>
        public int Id { get; }

        /// <summary>面板名称（诊断用）。</summary>
        public string Name { get; }

        /// <summary>面板 Prefab 地址。</summary>
        public AssetKey Prefab { get; }

        /// <summary>所在层：Page/Popup/Toast。</summary>
        public UiLayer Layer { get; set; } = UiLayer.Page;

        /// <summary>是否允许同一界面同时存在多个实例。</summary>
        public bool MultiInstance { get; set; }

        /// <summary>模态窗口仅允许放在 Popup 层；阻挡下方输入，但不自动隐藏下方内容。</summary>
        public bool Modal { get; set; }
        /// <summary>点遮罩是否关闭；仅模态允许。</summary>
        public bool CloseOnBackdrop { get; set; }
        /// <summary>返回键处理方式：关闭/拦截/忽略。</summary>
        public UiBackBehavior BackBehavior { get; set; } = UiBackBehavior.Close;

        /// <summary>成功关闭时是否允许按模块缓存策略保留实例。默认关闭后销毁。</summary>
        public bool CacheOnClose { get; set; }

        /// <summary>调试输出。</summary>
        public override string ToString() => "UiDef(" + Name + "#" + Id + ", " + Layer + ")";
    }
}
