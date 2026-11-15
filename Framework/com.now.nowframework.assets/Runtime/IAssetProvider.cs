using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Assets
{
    /// <summary>Provider 返回的底层句柄；`Release` 之后不得再使用其 Asset。</summary>
    public sealed class ProviderHandle
    {
        public ProviderHandle(string canonicalKey, UnityEngine.Object asset)
        {
            CanonicalKey = canonicalKey;
            Asset = asset;
        }

        public string CanonicalKey { get; }

        public UnityEngine.Object Asset { get; }

        public override string ToString() => "Handle(" + CanonicalKey + ")";
    }

    /// <summary>
    /// 可替换的资源 Provider（Addressables / YooAsset / 裸 AB）。
    ///
    /// 职责边界：
    /// 1. 把业务键规范化为 canonical 键（地址与 GUID 指向同一资源时必须相同）；
    /// 2. 真正加载与释放；
    /// 3. 不做引用计数——计数由 <see cref="AssetService"/> 负责。
    /// </summary>
    public interface IAssetProvider
    {
        UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct);

        /// <summary>加载；失败抛异常。实现方内部可缓存，但引用计数仍由 Service 负责。</summary>
        UniTask<ProviderHandle> LoadAsync(string canonicalKey, CancellationToken ct);

        /// <summary>释放底层句柄；幂等。</summary>
        void Release(ProviderHandle handle);
    }

    /// <summary>热更能力，可选：Provider 实现它才能用 Service 的热更 API。</summary>
    public interface IAssetUpdateProvider
    {
        UniTask<bool> CheckForUpdatesAsync(CancellationToken ct);
        UniTask<int> UpdateCatalogsAsync(CancellationToken ct);
        UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct);
        UniTask DownloadAsync(object key, System.IProgress<float> progress, CancellationToken ct);
    }
}