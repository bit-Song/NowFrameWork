using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Utility;

namespace NowFrameWork.Assets
{
    /// <summary>
    /// 资源服务实现：**引用计数核心**。Provider 只负责加载/释放，计数与并发合并在这里。
    ///
    /// 关键行为：
    /// 1. 同址并发只有一次底层加载（共享 Pending），每个等待者各得一个租约；
    /// 2. 单个等待者取消只影响自己（AttachExternalCancellation），底层加载继续；
    /// 3. 加载失败：不产生租约、清掉条目、异常抛给所有等待者，后续可重试；
    /// 4. 全部等待者都取消且加载最终完成 → 立即释放句柄，不留悬挂资源。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class AssetService : IAssetService, ILeaseOwner
    {
        sealed class Entry
        {
            public string Canonical;
            public AssetKey Key;
            public ProviderHandle Handle;
            public UniTaskCompletionSource<ProviderHandle> Pending;
            public int Leases;
            public int Waiters;
            public long Version;
        }

        readonly IAssetProvider _provider;
        readonly ILog _log;
        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _canonicalCache = new Dictionary<string, string>(StringComparer.Ordinal);

        int _pending;
        int _activeLeases;
        long _versionSeed;

        public AssetService(IAssetProvider provider, ILog log = null)
        {
            _provider = Guard.NotNull(provider, nameof(provider));
            _log = log ?? Logs.Null;
        }

        public int PendingRequests => _pending;

        public int ActiveLeases => _activeLeases;

        public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;

        public UniTask<AssetLease<T>> AcquireAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object
            => AcquireCoreAsync<T>(ToKey(key), AssetLeaseKind.Business, ct);

        public UniTask<AssetLease<T>> AcquireCacheAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object
            => AcquireCoreAsync<T>(ToKey(key), AssetLeaseKind.Cache, ct);

        async UniTask<AssetLease<T>> AcquireCoreAsync<T>(AssetKey key, AssetLeaseKind kind, CancellationToken ct)
            where T : UnityEngine.Object
        {
            ct.ThrowIfCancellationRequested();
            if (!key.IsValid) throw new ArgumentException("加载键为空");

            string canonical = await ResolveCanonicalAsync(key, ct);

            if (!_entries.TryGetValue(canonical, out Entry entry))
            {
                entry = new Entry { Canonical = canonical, Key = key, Version = ++_versionSeed };
                _entries.Add(canonical, entry);
            }

            if (entry.Handle != null) return Grant<T>(entry, kind);

            // 先登记等待者，再启动加载。
            // Provider 可能**同步完成**（缓存命中/假实现），此时 StartLoad 会一路跑到底；
            // 若顺序反过来，Waiters 还是 0 会被误判为"无人等待"而立即释放句柄。
            entry.Waiters++;
            try
            {
                // 持有局部引用：StartLoad 完成后会把 entry.Pending 置空
                UniTaskCompletionSource<ProviderHandle> pending = entry.Pending;
                if (pending == null)
                {
                    pending = new UniTaskCompletionSource<ProviderHandle>();
                    entry.Pending = pending;
                    _pending++;
                    StartLoad(entry).Forget();
                }

                // 单个等待者取消不影响共享加载
                await pending.Task.AttachExternalCancellation(ct);
                return Grant<T>(entry, kind);
            }
            finally
            {
                entry.Waiters--;
            }
        }

        AssetLease<T> Grant<T>(Entry entry, AssetLeaseKind kind) where T : UnityEngine.Object
        {
            var asset = entry.Handle.Asset as T;
            if (asset == null)
                throw new InvalidCastException("资源类型不匹配: " + entry.Canonical + " 期望 " + typeof(T).Name);

            entry.Leases++;
            _activeLeases++;

            return new AssetLease<T>(this, asset, entry.Key, entry.Canonical, entry.Version, kind);
        }

        async UniTask<string> ResolveCanonicalAsync(AssetKey key, CancellationToken ct)
        {
            string cacheKey = (key.Address ?? string.Empty) + "|" + (key.SubObject ?? string.Empty);
            if (_canonicalCache.TryGetValue(cacheKey, out string cached)) return cached;

            string canonical = await _provider.ResolveCanonicalKeyAsync(key, ct);
            if (string.IsNullOrEmpty(canonical))
                throw new InvalidOperationException("Provider 返回空 canonical 键: " + key.Address);

            _canonicalCache[cacheKey] = canonical;
            return canonical;
        }

        async UniTaskVoid StartLoad(Entry entry)
        {
            try
            {
                ProviderHandle handle = await _provider.LoadAsync(entry.Canonical, CancellationToken.None);
                _pending--;

                entry.Handle = handle;
                UniTaskCompletionSource<ProviderHandle> pending = entry.Pending;
                entry.Pending = null;
                pending.TrySetResult(handle);

                // 所有等待者都已取消：立即归还，避免悬挂资源
                if (entry.Waiters == 0 && entry.Leases == 0) ReleaseEntry(entry);
            }
            catch (Exception e)
            {
                _pending--;

                UniTaskCompletionSource<ProviderHandle> pending = entry.Pending;
                entry.Pending = null;
                _entries.Remove(entry.Canonical);
                pending.TrySetException(e);

                if (_log.IsEnabled(LogLevel.Error))
                    _log.Write(LogLevel.Error, "ASSET",
                        "加载失败: " + entry.Canonical + " -> " + e.GetType().Name + ": " + e.Message);
            }
        }

        void ILeaseOwner.ReleaseLease(string canonicalKey)
        {
            if (!_entries.TryGetValue(canonicalKey, out Entry entry)) return;

            entry.Leases--;
            _activeLeases--;

            if (entry.Leases > 0) return;
            ReleaseEntry(entry);
        }

        void ReleaseEntry(Entry entry)
        {
            if (entry.Handle != null)
            {
                _provider.Release(entry.Handle);
                entry.Handle = null;
            }

            if (entry.Canonical != null) _entries.Remove(entry.Canonical);
        }

        IAssetUpdateProvider UpdateProvider()
            => _provider as IAssetUpdateProvider
               ?? throw new NotSupportedException("当前 Provider 不支持热更: " + _provider.GetType().Name);

        public UniTask<bool> CheckForUpdatesAsync(CancellationToken ct) => UpdateProvider().CheckForUpdatesAsync(ct);

        public UniTask<int> UpdateCatalogsAsync(CancellationToken ct) => UpdateProvider().UpdateCatalogsAsync(ct);

        public UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct)
            => UpdateProvider().GetDownloadSizeAsync(key, ct);

        public UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct)
            => UpdateProvider().DownloadAsync(key, progress, ct);

        public void DumpLeases(List<LeaseInfo> buffer)
        {
            Guard.NotNull(buffer, nameof(buffer));
            buffer.Clear();

            foreach (KeyValuePair<string, Entry> pair in _entries)
                buffer.Add(new LeaseInfo(pair.Key, pair.Value.Leases, pair.Value.Version));
        }

        static AssetKey ToKey(object key)
        {
            switch (key)
            {
                case AssetKey assetKey: return assetKey;
                case string address: return AssetKey.From(address);
                case null: throw new ArgumentNullException(nameof(key));
                default: throw new ArgumentException("不支持的键类型: " + key.GetType().Name);
            }
        }
    }
}