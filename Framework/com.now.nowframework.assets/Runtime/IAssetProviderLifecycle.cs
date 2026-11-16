using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Assets
{
    /// <summary>可选的 Provider 初始化契约。仅主线程调用；完成后资源目录才可使用。</summary>
    public interface IAssetProviderLifecycle
    {
        UniTask InitializeAsync(CancellationToken ct);
    }
}
