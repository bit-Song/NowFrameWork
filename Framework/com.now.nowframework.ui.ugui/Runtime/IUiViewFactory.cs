using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NowFrameWork.UI
{
    /// <summary>
    /// 视图工厂：唯一接触资源加载与实例销毁的位置。
    ///
    /// 拆出这一层是为了**可测**：EditMode 用假实现（直接 new GameObject），
    /// 运行时用 AssetService 实现（加载 Prefab + 销毁等待 + 租约归还）。
    /// </summary>
    public interface IUiViewFactory
    {
        UniTask<UiView> CreateAsync(UiDefinition definition, Transform parent, CancellationToken ct);

        /// <summary>发起销毁（Unity 的 Destroy 延迟到帧末）；重复调用应安全。</summary>
        void Destroy(UiView view);

        /// <summary>
        /// 服务先调用 Destroy，再等待本方法确认销毁并归还租约。
        /// 返回 false 或抛异常时保留租约，允许用同一 view 重试；view 可能已是 Unity 伪空。
        /// </summary>
        UniTask<bool> ReleaseAsync(UiView view);
    }
}
