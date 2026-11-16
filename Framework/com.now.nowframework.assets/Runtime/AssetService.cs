using NowFrameWork.Core.Threading;
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
    public sealed class AssetService : IAssetService, IAssetUpdateProvider, ILeaseOwner
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
        int _resolving;
        bool _updatingCatalog;

        public AssetService(IAssetProvider provider, ILog log = null)
        {
            MainThreadGuard.Assert("AssetService.AssetService");
            _provider = Guard.NotNull(provider, nameof(provider));
            _log = log ?? Logs.Null;
        }

        public int PendingRequests
        {
            get
            {
                MainThreadGuard.Assert("AssetService.PendingRequests");
                return _pending;
            }
        }

        public int ActiveLeases
        {
            get
            {
                MainThreadGuard.Assert("AssetService.ActiveLeases");
                return _activeLeases;
            }
        }

        public UniTask InitializeAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("AssetService.InitializeAsync");
            ct.ThrowIfCancellationRequested();
            return _provider is IAssetProviderLifecycle lifecycle
                ? lifecycle.InitializeAsync(ct)
                : UniTask.CompletedTask;
        }

        public UniTask<AssetLease<T>> AcquireAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object
        {
            MainThreadGuard.Assert("AssetService.AcquireAsync");
            return AcquireCoreAsync<T>(ToKey(key), AssetLeaseKind.Business, ct);
        }

        public UniTask<AssetLease<T>> AcquireCacheAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object
        {
            MainThreadGuard.Assert("AssetService.AcquireCacheAsync");
            return AcquireCoreAsync<T>(ToKey(key), AssetLeaseKind.Cache, ct);
        }

        async UniTask<AssetLease<T>> AcquireCoreAsync<T>(AssetKey key, AssetLeaseKind kind, CancellationToken ct)
            where T : UnityEngine.Object
        {
            ct.ThrowIfCancellationRequested();
            if (!key.IsValid) throw new ArgumentException("加载键为空");
            if (_updatingCatalog) throw new InvalidOperationException("Catalog 更新期间不能加载资源");

            string canonical = await ResolveCanonicalAsync(key, typeof(T), ct);
            ct.ThrowIfCancellationRequested();

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

                // 取消恰好落在"加载完成"与"当前等待者恢复"之间时，StartLoad 里的检查会看到 Waiters == 1，
                // 之后没人接手，句柄就悬挂了。这里补一次同样条件的检查。
                if (entry.Waiters == 0 && entry.Leases == 0 && entry.Handle != null) ReleaseEntry(entry);
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

        async UniTask<string> ResolveCanonicalAsync(AssetKey key, Type assetType, CancellationToken ct)
        {
            string cacheKey = key.Address.Length + ":" + key.Address + (key.SubObject ?? string.Empty);
            if (_provider is ITypedAssetProvider) cacheKey += "|" + assetType.FullName;
            if (_canonicalCache.TryGetValue(cacheKey, out string cached)) return cached;

            _resolving++;
            try
            {
                string canonical = _provider is ITypedAssetProvider typed
                    ? await typed.ResolveCanonicalKeyAsync(key, assetType, ct)
                    : await _provider.ResolveCanonicalKeyAsync(key, ct);
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(canonical))
                    throw new InvalidOperationException("Provider 返回空 canonical 键: " + key.Address);
                _canonicalCache[cacheKey] = canonical;
                return canonical;
            }
            finally { _resolving--; }
        }

        async UniTaskVoid StartLoad(Entry entry)
        {
            UniTaskCompletionSource<ProviderHandle> pending = entry.Pending;
            ProviderHandle handle;
            try
            {
                handle = await _provider.LoadAsync(entry.Canonical, CancellationToken.None);
                if (handle == null) throw new InvalidOperationException("Provider 返回空句柄: " + entry.Canonical);
            }
            catch (Exception e)
            {
                _pending--;
                entry.Pending = null;
                if (_entries.TryGetValue(entry.Canonical, out Entry current) && ReferenceEquals(current, entry))
                    _entries.Remove(entry.Canonical);
                pending.TrySetException(e);
                if (_log.IsEnabled(LogLevel.Error))
                    _log.Write(LogLevel.Error, "ASSET", "加载失败: " + entry.Canonical + " -> " + e.Message);
                return;
            }

            _pending--;
            entry.Handle = handle;
            entry.Pending = null;
            pending.TrySetResult(handle);
            // 完成通知可以同步恢复多个等待者；不能把通知/释放异常当作第二次加载失败。
            if (entry.Waiters == 0 && entry.Leases == 0 && entry.Handle != null) ReleaseEntry(entry);
        }

        void ILeaseOwner.ReleaseLease(string canonicalKey, long version)
        {
            MainThreadGuard.Assert("AssetService.ReleaseLease");
            // 先扣总数：这个租约确实存在过，无论条目是否还在，总数都必须减一次。
            // 反过来（找不到条目就提前 return）会让 _activeLeases 永久偏高。
            _activeLeases--;

            if (!_entries.TryGetValue(canonicalKey, out Entry entry)) return;

            // 条目已被重建（旧租约在新世代才被释放）：只扣总数，不动新条目的计数
            if (entry.Version != version) return;

            entry.Leases--;

            if (entry.Leases > 0 || entry.Waiters > 0) return;
            ReleaseEntry(entry);
        }

        /// <summary>
        /// 计数不变量自检（测试与诊断用）：
        /// 1. `_activeLeases` 必须等于所有条目租约数之和；
        /// 2. 没有租约的条目不应留在表里。
        /// </summary>
        internal bool Validate(out string error)
        {
            int sum = 0;
            int orphan = 0;

            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                sum += pair.Value.Leases;
                if (pair.Value.Leases <= 0 && pair.Value.Waiters == 0 && pair.Value.Pending == null) orphan++;
            }

            if (sum != _activeLeases)
            {
                error = "ActiveLeases 漂移: 计数=" + _activeLeases + " 条目合计=" + sum;
                return false;
            }

            if (orphan > 0)
            {
                error = "存在 " + orphan + " 个零租约条目，应已移除";
                return false;
            }

            error = null;
            return true;
        }

        void ReleaseEntry(Entry entry)
        {
            ProviderHandle handle = entry.Handle;
            entry.Handle = null;
            if (_entries.TryGetValue(entry.Canonical, out Entry current) && ReferenceEquals(current, entry))
                _entries.Remove(entry.Canonical);
            if (handle != null) _provider.Release(handle);
        }

        IAssetUpdateProvider UpdateProvider()
        {
            MainThreadGuard.Assert("AssetService.UpdateProvider");
            return _provider as IAssetUpdateProvider
               ?? throw new NotSupportedException("当前 Provider 不支持热更: " + _provider.GetType().Name);
        }

        public UniTask<bool> CheckForUpdatesAsync(CancellationToken ct) => UpdateProvider().CheckForUpdatesAsync(ct);

        public async UniTask<int> UpdateCatalogsAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("AssetService.UpdateCatalogsAsync");
            if (_entries.Count != 0 || _resolving != 0 || _updatingCatalog)
                throw new InvalidOperationException("更新 Catalog 前必须归还全部租约并结束在途加载");
            _updatingCatalog = true;
            try
            {
                int updated = await UpdateProvider().UpdateCatalogsAsync(ct);
                _canonicalCache.Clear();
                return updated;
            }
            finally { _updatingCatalog = false; }
        }

        public UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct)
            => UpdateProvider().GetDownloadSizeAsync(key, ct);

        public UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct)
            => UpdateProvider().DownloadAsync(key, progress, ct);

        public void DumpLeases(List<LeaseInfo> buffer)
        {
            MainThreadGuard.Assert("AssetService.DumpLeases");
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
