using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace NowFrameWork.Assets.Adapter
{
    /// <summary>
    /// Addressables 场景服务：附加加载 + 异步卸载。
    ///
    /// 与资源服务共用同一套超时策略——远端场景在服务器不可达时同样不能无限挂起。
    /// </summary>
    public sealed class AddressablesSceneService : ISceneService
    {
        readonly TimeSpan _timeout;

        public AddressablesSceneService(TimeSpan? timeout = null)
        {
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        public async UniTask<SceneLease> LoadAdditiveAsync(object key, CancellationToken ct)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            AsyncOperationHandle<SceneInstance> handle =
                UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(key, LoadSceneMode.Additive);

            await AwaitHandle(handle, ct);

            return new SceneLease(key.ToString(), async () =>
            {
                AsyncOperationHandle<SceneInstance> unload =
                    UnityEngine.AddressableAssets.Addressables.UnloadSceneAsync(handle);

                await AwaitHandle(unload, CancellationToken.None);
            });
        }

        async UniTask AwaitHandle<T>(AsyncOperationHandle<T> handle, CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource<T>();
            CancellationTokenSource cts = null;

            if (_timeout > TimeSpan.Zero)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);
                cts.Token.Register(() => tcs.TrySetException(
                    new TimeoutException("场景操作超时（" + _timeout.TotalSeconds + "s）")));
            }

            handle.Completed += op =>
            {
                cts?.Dispose();

                if (op.Status == AsyncOperationStatus.Succeeded) tcs.TrySetResult(op.Result);
                else tcs.TrySetException(op.OperationException ?? new InvalidOperationException("场景操作失败"));
            };

            await tcs.Task;
        }
    }
}