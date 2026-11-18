using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Boot;
using NowFrameWork.Generated;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// Boot 项目入口示例：把本组件配置到 BootFlow 的 Project Entry 后运行。
    /// 复用 Boot 的资源服务，通过 GameEntry 加载；停止时先销毁实例，再归还租约。
    /// </summary>
    public sealed class AssetLoadDemo : MonoBehaviour, IBootEntry
    {
        GameEntry.Binding _binding;
        AssetLease<GameObject> _lease;
        GameObject _instance;

        public async UniTask EnterAsync(BootContext context, CancellationToken ct)
        {
            if (_binding != null) throw new InvalidOperationException("资源示例尚未退出。");
            ct.ThrowIfCancellationRequested();
            _binding = GameEntry.Bind(context);
            _lease = await GameEntry.Assets.AcquireAsync<GameObject>(
                AssetKeys.Tests.PCRegression.Probe_prefab, ct);
            ct.ThrowIfCancellationRequested();
            _instance = Instantiate(_lease.Asset);
            Debug.Log("[GameEntry 加载示例] 已创建实例；点击 Boot 停止后销毁并归还租约。", this);
        }

        public async UniTask ExitAsync()
        {
            _binding?.BeginStopping();
            if (_instance != null) Destroy(_instance);
            if (!await UnityObjectLifecycle.WaitDestroyedAsync(_instance))
                throw new TimeoutException("资源示例实例尚未销毁，保留租约供再次停止时重试。");
            _instance = null;
            _lease?.Dispose(); _lease = null;
            _binding?.Dispose(); _binding = null;
        }
    }
}
