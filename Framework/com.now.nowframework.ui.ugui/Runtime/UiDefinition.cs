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
        public UiDefinition(int id, string name, AssetKey prefab)
        {
            Id = id;
            Name = name;
            Prefab = prefab;
        }

        public int Id { get; }

        public string Name { get; }

        public AssetKey Prefab { get; }

        public UiLayer Layer { get; set; } = UiLayer.Page;

        /// <summary>是否允许同一界面同时存在多个实例。</summary>
        public bool MultiInstance { get; set; }

        /// <summary>关闭后是否进入有界缓存（UI-4 实现，先记录契约）。</summary>
        public bool CacheOnClose { get; set; }

        public override string ToString() => "UiDef(" + Name + "#" + Id + ", " + Layer + ")";
    }
}