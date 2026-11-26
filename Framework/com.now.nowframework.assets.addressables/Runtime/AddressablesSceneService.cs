using NowFrameWork.Core.Threading;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace NowFrameWork.Assets.Adapter
{
    /// <summary>仅主线程。取消只结束调用方等待，晚到的场景仍由服务负责卸载。</summary>
    public sealed class AddressablesSceneService : ISceneService
    {
        readonly TimeSpan _timeout;
        readonly AddressablesProvider _provider;

        /// <summary>创建场景服务，使用默认提供者配置。</summary>
        public AddressablesSceneService(TimeSpan? timeout = null) : this(null, timeout) { }

        /// <summary>复用已有提供者的 Catalog 配置。</summary>
        public AddressablesSceneService(AddressablesProvider provider, TimeSpan? timeout)
        {
            MainThreadGuard.Assert("AddressablesSceneService.AddressablesSceneService");
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
            _provider = provider;
        }

        /// <summary>附加加载场景并返回租约。</summary>
        public async UniTask<SceneLease> LoadAdditiveAsync(object key, CancellationToken ct)
        {
            MainThreadGuard.Assert("AddressablesSceneService.LoadAdditiveAsync");
            if (key == null) throw new ArgumentNullException(nameof(key));
            ct.ThrowIfCancellationRequested();
            var location = _provider?.ResolveReleaseLocation(key, typeof(SceneInstance));
            var handle = location == null ? Addressables.LoadSceneAsync(key, LoadSceneMode.Additive)
                : Addressables.LoadSceneAsync(location, LoadSceneMode.Additive);
            var owner = new SceneOwner(handle, _timeout);
            try
            {
                await AddressablesOperation.WaitAsync(handle, _timeout, ct);
                return new SceneLease(key.ToString(), owner.UnloadAsync);
            }
            catch
            {
                owner.CleanupAbandonedAsync().Forget();
                throw;
            }
        }

        sealed class SceneOwner
        {
            readonly AsyncOperationHandle<SceneInstance> _load;
            readonly TimeSpan _timeout;
            UniTaskCompletionSource _unloading;
            bool _unloaded;

            /// <summary>场景加载操作的所有者。</summary>
            public SceneOwner(AsyncOperationHandle<SceneInstance> load, TimeSpan timeout)
            {
                _load = load;
                _timeout = timeout;
            }

            /// <summary>卸载场景并等待完成。</summary>
            public async UniTask UnloadAsync()
            {
                if (_unloaded) return;
                UniTaskCompletionSource completion = _unloading;
                if (completion == null)
                {
                    completion = new UniTaskCompletionSource();
                    _unloading = completion;
                    PerformUnloadAsync(completion).Forget();
                }
                await AddressablesOperation.WaitAsync(completion.Task, _timeout, CancellationToken.None);
            }

            async UniTask PerformUnloadAsync(UniTaskCompletionSource completion)
            {
                AsyncOperationHandle<SceneInstance> operation = default;
                try
                {
                    operation = Addressables.UnloadSceneAsync(_load, false);
                    // 不因外层超时重复卸载。实际操作继续完成并归还其句柄。
                    await AddressablesOperation.WaitAsync(operation, TimeSpan.Zero, CancellationToken.None);
                    _unloaded = true;
                    completion.TrySetResult();
                }
                catch (Exception e)
                {
                    // 若加载句柄仍有效，允许租约在真实卸载失败后重试。
                    if (_load.IsValid()) _unloading = null;
                    completion.TrySetException(e);
                }
                finally { AddressablesOperation.Release(operation); }
            }

            /// <summary>清理未正常卸载的残留场景。</summary>
            public async UniTask CleanupAbandonedAsync()
            {
                try
                {
                    await AddressablesOperation.WaitAsync(_load, TimeSpan.Zero, CancellationToken.None);
                    var operation = Addressables.UnloadSceneAsync(_load, false);
                    try { await AddressablesOperation.WaitAsync(operation, TimeSpan.Zero, CancellationToken.None); }
                    finally { AddressablesOperation.Release(operation); }
                }
                catch (Exception e)
                {
                    AddressablesOperation.Release(_load);
                    UnityEngine.Debug.LogException(e);
                }
            }
        }
    }
}
