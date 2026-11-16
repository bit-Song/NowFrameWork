using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Utility;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace NowFrameWork.Assets.Adapter
{
    /// <summary>Addressables Provider。仅主线程；每次请求的句柄只有一个释放责任人。</summary>
    public sealed class AddressablesProvider : IAssetProvider, IAssetUpdateProvider, IAssetProviderLifecycle, ITypedAssetProvider
    {
        readonly TimeSpan _timeout;
        readonly Dictionary<string, IResourceLocation> _locations = new Dictionary<string, IResourceLocation>(StringComparer.Ordinal);

        public AddressablesProvider(TimeSpan? timeout = null)
        {
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var handle = Addressables.InitializeAsync(false);
            try { await AddressablesOperation.WaitAsync(handle, _timeout, ct); }
            finally { AddressablesOperation.Release(handle); }
        }

        public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
            => ResolveCanonicalKeyAsync(key, typeof(UnityEngine.Object), ct);

        public async UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, Type assetType, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Guard.NotNullOrEmpty(key.Address, nameof(key));
            string address = string.IsNullOrEmpty(key.SubObject) ? key.Address : key.Address + "[" + key.SubObject + "]";
            var handle = Addressables.LoadResourceLocationsAsync(address, assetType);
            try
            {
                await AddressablesOperation.WaitAsync(handle, _timeout, ct);
                IList<IResourceLocation> locations = handle.Result;
                if (locations == null || locations.Count == 0)
                    throw new InvalidOperationException("找不到资源: " + address);
                if (locations.Count != 1)
                    throw new InvalidOperationException("资源键不唯一，不支持以标签加载单个资源: " + address);
                IResourceLocation location = locations[0];
                string canonical = location.PrimaryKey + "|" + location.ResourceType.FullName;
                _locations[canonical] = location;
                return canonical;
            }
            finally { AddressablesOperation.Release(handle); }
        }

        public async UniTask<ProviderHandle> LoadAsync(string canonicalKey, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_locations.TryGetValue(canonicalKey, out IResourceLocation location))
                throw new InvalidOperationException("资源须先完成定位: " + canonicalKey);
            var handle = Addressables.LoadAssetAsync<UnityEngine.Object>(location);
            try
            {
                await AddressablesOperation.WaitAsync(handle, _timeout, ct);
                // 成功时交给 ProviderHandle 持有，直到最后一个租约归还。
                return new ProviderHandle(canonicalKey, handle.Result, handle);
            }
            catch
            {
                AddressablesOperation.Release(handle);
                throw;
            }
        }

        public void Release(ProviderHandle handle)
        {
            if (handle?.Tag is AsyncOperationHandle<UnityEngine.Object> op)
                AddressablesOperation.Release(op);
        }

        public async UniTask<bool> CheckForUpdatesAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var handle = Addressables.CheckForCatalogUpdates(false);
            try
            {
                await AddressablesOperation.WaitAsync(handle, _timeout, ct);
                return handle.Result != null && handle.Result.Count > 0;
            }
            finally { AddressablesOperation.Release(handle); }
        }

        public async UniTask<int> UpdateCatalogsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var handle = Addressables.UpdateCatalogs(null, false);
            try
            {
                await AddressablesOperation.WaitAsync(handle, _timeout, ct);
                return handle.Result?.Count ?? 0;
            }
            finally { AddressablesOperation.Release(handle); }
        }

        public async UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var handle = Addressables.GetDownloadSizeAsync(key);
            try
            {
                await AddressablesOperation.WaitAsync(handle, _timeout, ct);
                return handle.Result;
            }
            finally { AddressablesOperation.Release(handle); }
        }

        public async UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var handle = Addressables.DownloadDependenciesAsync(key, false);
            try { await AddressablesOperation.WaitAsync(handle, _timeout, ct, progress); }
            finally { AddressablesOperation.Release(handle); }
        }
    }
}
