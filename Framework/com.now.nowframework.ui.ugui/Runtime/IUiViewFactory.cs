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

        /// <summary>发起销毁（Unity 的 Destroy 延迟到帧末）。</summary>
        void Destroy(UiView view);

        /// <summary>等实例真的消失后再归还租约；返回 false 表示超时未销毁。</summary>
        UniTask<bool> ReleaseAsync(UiView view);
    }
}