using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Assets
{
    /// <summary>
    /// 一份场景租约：持有一个已加载的场景。
    ///
    /// 与 <see cref="AssetLease{T}"/> 同构——不归还就不卸载；
    /// 卸载是**异步**的，调用方必须 await（`UnityEngine.SceneManagement` 的卸载跨帧完成）。
    /// </summary>
    public sealed class SceneLease
    {
        readonly Func<UniTask> _unload;

        internal SceneLease(string key, Func<UniTask> unload)
        {
            Key = key;
            _unload = unload;
        }

        public string Key { get; }

        public bool IsUnloaded { get; private set; }

        /// <summary>卸载场景；幂等。</summary>
        public async UniTask UnloadAsync()
        {
            if (IsUnloaded) return;

            IsUnloaded = true;
            await _unload();
        }

        public override string ToString() => "SceneLease(" + Key + (IsUnloaded ? ", unloaded" : "") + ")";
    }

    /// <summary>
    /// 场景服务：附加加载与卸载。
    ///
    /// 约定（[13 §5.4]）：
    /// 1. 只有 additive 加载——单场景切换由 Scope 负责组织；
    /// 2. 卸载必须 await（跨帧）；
    /// 3. 场景内的资源由该场景对应的 Scope 管理，不在这里处理。
    /// </summary>
    public interface ISceneService
    {
        UniTask<SceneLease> LoadAdditiveAsync(object key, CancellationToken ct);
    }
}