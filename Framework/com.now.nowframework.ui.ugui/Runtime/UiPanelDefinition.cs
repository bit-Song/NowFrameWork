using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>必须挂在面板 Prefab 的根节点：声明逻辑面板的 ID 与行为，供编辑器收集进 UiCatalog；子节点上的会被忽略。</summary>
    [DisallowMultipleComponent]
    public sealed class UiPanelDefinition : MonoBehaviour
    {
        [Tooltip("逻辑面板 ID，全工程唯一；0 表示未填写，收集时会报错。")]
        public int PanelId;

        [Tooltip("所在层：Page / Popup / Toast。")]
        public UiLayer Layer = UiLayer.Page;

        [Tooltip("是否允许同一 ID 同时打开多个实例。")]
        public bool MultiInstance;

        [Tooltip("是否模态：显示遮罩并阻挡下层输入，仅 Popup 层有效。")]
        public bool Modal;

        [Tooltip("点遮罩是否关闭；仅在 Modal 时有效。")]
        public bool CloseOnBackdrop;

        [Tooltip("返回键处理方式。")]
        public UiBackBehavior BackBehavior = UiBackBehavior.Close;

        [Tooltip("关闭后是否进入缓存以便复用。")]
        public bool CacheOnClose;
    }
}
