using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace NowFrameWork.Assets.Adapter
{
    /// <summary>只等待、不转移句柄所有权。超时与取消在 Unity PlayerLoop 中检查，释放由调用方负责。</summary>
    internal static class AddressablesOperation
    {
        public static async UniTask WaitAsync(AsyncOperationHandle handle, TimeSpan timeout,
            CancellationToken ct, IProgress<float> progress = null)
        {
            ct.ThrowIfCancellationRequested();
            long start = Stopwatch.GetTimestamp();
            while (!handle.IsDone)
            {
                ct.ThrowIfCancellationRequested();
                if (timeout > TimeSpan.Zero &&
                    (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency >= timeout.TotalSeconds)
                    throw new TimeoutException("Addressables 操作超时（" + timeout.TotalSeconds + "s）");
                progress?.Report(handle.GetDownloadStatus().Percent);
                await UniTask.Yield();
            }
            ct.ThrowIfCancellationRequested();
            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw handle.OperationException ?? new InvalidOperationException("Addressables 操作失败");
            progress?.Report(1f);
        }

        public static void Release(AsyncOperationHandle handle)
        {
            if (handle.IsValid()) UnityEngine.AddressableAssets.Addressables.Release(handle);
        }

        public static async UniTask WaitAsync(UniTask task, TimeSpan timeout, CancellationToken ct)
        {
            long start = Stopwatch.GetTimestamp();
            while (task.Status == UniTaskStatus.Pending)
            {
                ct.ThrowIfCancellationRequested();
                if (timeout > TimeSpan.Zero &&
                    (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency >= timeout.TotalSeconds)
                    throw new TimeoutException("场景卸载仍在进行；稍后可重试等待");
                await UniTask.Yield();
            }
            ct.ThrowIfCancellationRequested();
            await task;
        }
    }
}
