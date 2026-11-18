using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.UI
{
    /// <summary>仅主线程的 UI 契约。句柄不拥有资源；Close/显隐/导航的调用者取消只结束等待，Shutdown 等待服务收尾。</summary>
    public interface IUiService
    {
        int OpeningCount { get; }
        int VisibleCount { get; }
        int HiddenCount { get; }
        int FaultedCount { get; }
        int SessionCount { get; }
        int CachedCount { get; }
        UiNavigator Navigation { get; }
        UiHandle TopPopup { get; }
        UiHandle TopModal { get; }
        UniTask<bool> RequestBackAsync(CancellationToken ct = default);
        UniTask<UiOpenResult> OpenAsync<TArgs>(UiKey<TArgs> key, TArgs args, CancellationToken ct = default) where TArgs : class;
        UniTask ShowAsync(UiHandle handle, CancellationToken ct = default);
        UniTask HideAsync(UiHandle handle, CancellationToken ct = default);
        UniTask CloseAsync(UiHandle handle, CancellationToken ct = default);
        /// <summary>清理调用时的空闲缓存。取消只结束等待，清理仍由服务持有。</summary>
        UniTask TrimCacheAsync(CancellationToken ct = default);
        UiVisibilityState GetVisibilityState(UiHandle handle);
        UniTask ShutdownAsync();
    }
}
