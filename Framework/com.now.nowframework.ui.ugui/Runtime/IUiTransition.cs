using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.UI
{
    /// <summary>
    /// 视觉过渡：可等待、可取消。
    ///
    /// 约定：实现方只驱动本界面的显示属性，**不管理资源卸载与全局返回栈**；
    /// 取消时必须停止写入并抛出 OperationCanceledException。
    /// </summary>
    public interface IUiTransition
    {
        UniTask ShowAsync(UiView view, CancellationToken ct);

        UniTask HideAsync(UiView view, CancellationToken ct);
    }

    /// <summary>立即完成：无动画时的默认实现。</summary>
    public sealed class ImmediateUiTransition : IUiTransition
    {
        public static readonly ImmediateUiTransition Instance = new ImmediateUiTransition();

        public UniTask ShowAsync(UiView view, CancellationToken ct) => UniTask.CompletedTask;

        public UniTask HideAsync(UiView view, CancellationToken ct) => UniTask.CompletedTask;
    }
}