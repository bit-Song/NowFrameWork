using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.UI
{
    /// <summary>仅主线程的 UI-1 契约。句柄不拥有资源；Close/显隐的 ct 只取消调用者等待，Shutdown 等待服务收尾。</summary>
    public interface IUiService
    {
        int OpeningCount { get; }
        int VisibleCount { get; }
        int HiddenCount { get; }
        int FaultedCount { get; }
        int SessionCount { get; }
        UniTask<UiOpenResult> OpenAsync<TArgs>(UiKey<TArgs> key, TArgs args, CancellationToken ct = default) where TArgs : class;
        UniTask ShowAsync(UiHandle handle, CancellationToken ct = default);
        UniTask HideAsync(UiHandle handle, CancellationToken ct = default);
        UniTask CloseAsync(UiHandle handle, CancellationToken ct = default);
        UiVisibilityState GetVisibilityState(UiHandle handle);
        UniTask ShutdownAsync();
    }
}
