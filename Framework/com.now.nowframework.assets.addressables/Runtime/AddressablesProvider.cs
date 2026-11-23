using NowFrameWork.Core.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Utility;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace NowFrameWork.Assets.Adapter
{
    /// <summary>Addressables Provider。仅主线程；每次请求的句柄只有一个释放责任人。</summary>
    public sealed class AddressablesProvider : IAssetProvider, IAssetUpdateProvider, IAssetProviderLifecycle, ITypedAssetProvider
    {
        readonly TimeSpan _timeout;
        readonly ReleaseSelection _release;
        IResourceLocator _releaseLocator;
        readonly Dictionary<string, IResourceLocation> _locations = new Dictionary<string, IResourceLocation>(StringComparer.Ordinal);

        public AddressablesProvider(TimeSpan? timeout = null) : this(null, timeout) { }

        public AddressablesProvider(ReleaseSelection release, TimeSpan? timeout)
        {
            MainThreadGuard.Assert("AddressablesProvider.AddressablesProvider");
            _release = release;
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("AddressablesProvider.InitializeAsync");
            ct.ThrowIfCancellationRequested();
            var handle = Addressables.InitializeAsync(false);
            try { await AddressablesOperation.WaitAsync(handle, _timeout, ct); }
            finally { AddressablesOperation.Release(handle); }
            if (_release == null || _releaseLocator != null) return;

            AddressablesRuntimeProperties.SetPropertyValue("NowFrameWork.ReleaseRoot", _release.Directory.Replace('\\', '/'));
            var catalog = Addressables.LoadContentCatalogAsync(_release.CatalogPath, false);
            try
            {
                await AddressablesOperation.WaitAsync(catalog, _timeout, ct);
                CheckReleaseBundles(catalog.Result);
                _releaseLocator = catalog.Result;
            }
            catch
            {
                if (catalog.IsDone) RemoveAbandonedCatalog(catalog);
                else catalog.Completed += RemoveAbandonedCatalog;
                throw;
            }
            AddressablesOperation.Release(catalog);
        }

        void CheckReleaseBundles(IResourceLocator locator)
        {
            var checkedLocations = new HashSet<IResourceLocation>();
            foreach (object key in locator.Keys)
            {
                if (!locator.Locate(key, null, out var locations)) continue;
                foreach (var location in locations) CheckLocation(location, checkedLocations);
            }
        }

        void CheckLocation(IResourceLocation location, HashSet<IResourceLocation> checkedLocations)
        {
            if (!checkedLocations.Add(location)) return;
            if (location.ResourceType == typeof(IAssetBundleResource) && !_release.ContainsFile(location.InternalId))
                throw new InvalidOperationException("Catalog 引用了发布清单之外的 Bundle: " + location.InternalId);
            if (location.HasDependencies)
                foreach (var dependency in location.Dependencies) CheckLocation(dependency, checkedLocations);
        }

        static void RemoveAbandonedCatalog(AsyncOperationHandle<IResourceLocator> operation)
        {
            if (operation.Status == AsyncOperationStatus.Succeeded)
                Addressables.RemoveResourceLocator(operation.Result);
            AddressablesOperation.Release(operation);
        }

        /// <summary>在 Boot 清理资源和场景成功后解除本次选定的目录。</summary>
        public void RemoveReleaseCatalog()
        {
            MainThreadGuard.Assert();
            if (_releaseLocator == null) return;
            Addressables.RemoveResourceLocator(_releaseLocator);
            _releaseLocator = null;
            _locations.Clear();
        }

        internal IResourceLocation ResolveReleaseLocation(object key, Type type)
        {
            if (_release == null) return null;
            if (_releaseLocator == null || !_releaseLocator.Locate(key, type, out var locations) || locations.Count != 1)
                throw new InvalidOperationException("发布目录中找不到唯一资源: " + key);
            return locations[0];
        }

        public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
            => ResolveCanonicalKeyAsync(key, typeof(UnityEngine.Object), ct);

        public async UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, Type assetType, CancellationToken ct)
        {
            MainThreadGuard.Assert("AddressablesProvider.ResolveCanonicalKeyAsync");
            ct.ThrowIfCancellationRequested();
            Guard.NotNullOrEmpty(key.Address, nameof(key));
            string address = string.IsNullOrEmpty(key.SubObject) ? key.Address : key.Address + "[" + key.SubObject + "]";
            IResourceLocation releaseLocation = ResolveReleaseLocation(address, assetType);
            if (releaseLocation != null) return Remember(releaseLocation);
            var handle = Addressables.LoadResourceLocationsAsync(address, assetType);
            try
            {
                await AddressablesOperation.WaitAsync(handle, _timeout, ct);
                IList<IResourceLocation> locations = handle.Result;
                if (locations == null || locations.Count == 0)
                    throw new InvalidOperationException("找不到资源: " + address);
                if (locations.Count != 1)
                    throw new InvalidOperationException("资源键不唯一，不支持以标签加载单个资源: " + address);
                return Remember(locations[0]);
            }
            finally { AddressablesOperation.Release(handle); }
        }

        string Remember(IResourceLocation location)
        {
            string canonical = location.PrimaryKey + "|" + location.ResourceType.FullName;
            _locations[canonical] = location;
            return canonical;
        }

        public async UniTask<ProviderHandle> LoadAsync(string canonicalKey, CancellationToken ct)
        {
            MainThreadGuard.Assert("AddressablesProvider.LoadAsync");
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
            MainThreadGuard.Assert("AddressablesProvider.Release");
            if (handle?.Tag is AsyncOperationHandle<UnityEngine.Object> op)
                AddressablesOperation.Release(op);
        }

        public async UniTask<bool> CheckForUpdatesAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("AddressablesProvider.CheckForUpdatesAsync");
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
            MainThreadGuard.Assert("AddressablesProvider.UpdateCatalogsAsync");
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
            MainThreadGuard.Assert("AddressablesProvider.GetDownloadSizeAsync");
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
            MainThreadGuard.Assert("AddressablesProvider.DownloadAsync");
            ct.ThrowIfCancellationRequested();
            var handle = Addressables.DownloadDependenciesAsync(key, false);
            try { await AddressablesOperation.WaitAsync(handle, _timeout, ct, progress); }
            finally { AddressablesOperation.Release(handle); }
        }
    }
}
