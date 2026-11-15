using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Utility;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

// 注意：命名空间不能以 .Addressables 结尾，否则会遮蔽 UnityEngine.AddressableAssets.Addressables 类
namespace NowFrameWork.Assets.Adapter
{
    /// <summary>
    /// Addressables 实现。全工程唯一直接引用 Addressables 的类。
    ///
    /// 三个与 [13](../../NowFrameWork/Assets/docs/13-Scope与资源层设计.md) 对应的实现选择：
    /// 1. **canonical 键用 PrimaryKey**：地址与 GUID 指向同一资源时 PrimaryKey 相同，计数不会分裂；
    /// 2. **取消只放弃等待**：单个等待者取消不取消底层共享加载（由 AssetService 保证语义）；
    /// 3. **Release 后资源立即失效**：Addressables 用 AssetBundle.Unload(true)，故销毁实例与释放租约必须跨帧（R-1）。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class AddressablesProvider : IAssetProvider, IAssetUpdateProvider
    {
        public async UniTask InitializeAsync(CancellationToken ct)
        {
            await AwaitHandle(Addressables.InitializeAsync(), ct);
        }

        public async UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
        {
            Guard.NotNullOrEmpty(key.Address, nameof(key));

            AsyncOperationHandle<IList<IResourceLocation>> handle =
                Addressables.LoadResourceLocationsAsync(key.Address, typeof(UnityEngine.Object));

            IList<IResourceLocation> locations = await AwaitHandle(handle, ct);

            try
            {
                if (locations == null || locations.Count == 0)
                    throw new InvalidOperationException("找不到资源: " + key.Address);

                return locations[0].PrimaryKey;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        public async UniTask<ProviderHandle> LoadAsync(string canonicalKey, CancellationToken ct)
        {
            AsyncOperationHandle<UnityEngine.Object> handle =
                Addressables.LoadAssetAsync<UnityEngine.Object>(canonicalKey);

            UnityEngine.Object asset = await AwaitHandle(handle, ct);
            return new ProviderHandle(canonicalKey, asset, handle);
        }

        public void Release(ProviderHandle handle)
        {
            if (handle == null) return;

            if (handle.Tag is AsyncOperationHandle<UnityEngine.Object> op) Addressables.Release(op);
        }

        public async UniTask<bool> CheckForUpdatesAsync(CancellationToken ct)
        {
            AsyncOperationHandle<List<string>> handle = Addressables.CheckForCatalogUpdates(false);
            List<string> catalogs = await AwaitHandle(handle, ct);

            try
            {
                return catalogs != null && catalogs.Count > 0;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        public async UniTask<int> UpdateCatalogsAsync(CancellationToken ct)
        {
            AsyncOperationHandle<List<string>> handle = Addressables.UpdateCatalogs(null, false);
            List<string> updated = await AwaitHandle(handle, ct);

            try
            {
                return updated?.Count ?? 0;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        public async UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct)
        {
            AsyncOperationHandle<long> handle = Addressables.GetDownloadSizeAsync(key);

            try
            {
                return await AwaitHandle(handle, ct);
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        public async UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct)
        {
            AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(key);

            if (progress != null) handle.Completed += _ => progress.Report(1f);

            try
            {
                await AwaitHandle(handle, ct);
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        /// <summary>桥接 Addressables 泛型操作；取消仅放弃等待，底层操作继续（共享加载语义）。</summary>
        static UniTask<T> AwaitHandle<T>(AsyncOperationHandle<T> handle, CancellationToken ct)
        {
            if (handle.IsDone)
            {
                if (handle.Status == AsyncOperationStatus.Succeeded) return UniTask.FromResult(handle.Result);

                Exception failed = handle.OperationException ?? new InvalidOperationException("Addressables 操作失败");
                Addressables.Release(handle);
                return UniTask.FromException<T>(failed);
            }

            var tcs = new UniTaskCompletionSource<T>();
            handle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    tcs.TrySetResult(op.Result);
                }
                else
                {
                    Exception failed = op.OperationException ?? new InvalidOperationException("Addressables 操作失败");
                    Addressables.Release(op);
                    tcs.TrySetException(failed);
                }
            };

            return tcs.Task;
        }

        /// <summary>桥接非泛型操作。</summary>
        static UniTask AwaitHandle(AsyncOperationHandle handle, CancellationToken ct)
        {
            if (handle.IsDone)
            {
                if (handle.Status == AsyncOperationStatus.Succeeded) return UniTask.CompletedTask;

                Exception failed = handle.OperationException ?? new InvalidOperationException("Addressables 操作失败");
                Addressables.Release(handle);
                return UniTask.FromException(failed);
            }

            var tcs = new UniTaskCompletionSource();
            handle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    tcs.TrySetResult();
                }
                else
                {
                    Exception failed = op.OperationException ?? new InvalidOperationException("Addressables 操作失败");
                    Addressables.Release(op);
                    tcs.TrySetException(failed);
                }
            };

            return tcs.Task;
        }
    }
}