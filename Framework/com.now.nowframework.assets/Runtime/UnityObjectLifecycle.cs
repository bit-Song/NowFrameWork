using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.Assets
{
    /// <summary>仅主线程。帧数是等待预算，只有对象实际销毁才返回 true；超时不释放任何资源。</summary>
    public static class UnityObjectLifecycle
    {
        /// <summary>等待实例真正销毁；超时返回 false。</summary>
        public static UniTask<bool> WaitDestroyedAsync(UnityEngine.Object instance, int maxFrames = 60, Func<UniTask> frameWaiter = null)
            => WaitDestroyedAsync(() => instance == null, maxFrames, frameWaiter);

        /// <summary>等待一组实例全部销毁；超时返回 false。</summary>
        public static UniTask<bool> WaitAllDestroyedAsync<T>(IReadOnlyList<T> instances, int maxFrames = 60,
            Func<UniTask> frameWaiter = null) where T : UnityEngine.Object
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            return WaitDestroyedAsync(() =>
            {
                for (int i = 0; i < instances.Count; i++) if (instances[i] != null) return false;
                return true;
            }, maxFrames, frameWaiter);
        }

        /// <summary>按自定义判定等待销毁。</summary>
        public static async UniTask<bool> WaitDestroyedAsync(Func<bool> isDestroyed, int maxFrames,
            Func<UniTask> frameWaiter = null)
        {
            MainThreadGuard.Assert("UnityObjectLifecycle.WaitDestroyedAsync");
            if (isDestroyed == null) throw new ArgumentNullException(nameof(isDestroyed));
            if (maxFrames < 0) throw new ArgumentOutOfRangeException(nameof(maxFrames));
            for (int frame = 0; !isDestroyed(); frame++)
            {
                if (frame >= maxFrames) return false;
                if (frameWaiter != null) await frameWaiter();
                else await UniTask.NextFrame();
            }
            return true;
        }
    }
}
