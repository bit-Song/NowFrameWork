using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Assets
{
    /// <summary>诊断用：一条聚合后的租约记录。</summary>
    public readonly struct LeaseInfo
    {
        public LeaseInfo(string canonicalKey, int leases, long version)
        {
            CanonicalKey = canonicalKey;
            Leases = leases;
            Version = version;
        }

        public string CanonicalKey { get; }
        public int Leases { get; }
        public long Version { get; }

        public override string ToString() => CanonicalKey + " x" + Leases + " v" + Version;
    }

    /// <summary>
    /// 资源服务：业务唯一的资源入口（禁止直接调用 Addressables）。
    ///
    /// 租约语义见 [13 §2]：
    /// 1. 同址并发请求共享底层加载，但各得独立租约；
    /// 2. 取消只结束该等待者；
    /// 3. 失败不产生租约，且底层句柄要回收。
    /// </summary>
    public interface IAssetService
    {
        UniTask InitializeAsync(CancellationToken ct);

        UniTask<AssetLease<T>> AcquireAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object;

        /// <summary>缓存保留：用于池/常驻资源保活，不计入业务租约统计。</summary>
        UniTask<AssetLease<T>> AcquireCacheAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object;

        UniTask<bool> CheckForUpdatesAsync(CancellationToken ct);
        UniTask<int> UpdateCatalogsAsync(CancellationToken ct);
        UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct);
        UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct);

        /// <summary>进行中的底层加载数量。</summary>
        int PendingRequests { get; }

        /// <summary>当前有效租约数量（业务 + 缓存）。</summary>
        int ActiveLeases { get; }

        void DumpLeases(List<LeaseInfo> buffer);
    }
}