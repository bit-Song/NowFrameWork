using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core;
using NowFrameWork.Core.Logging;

namespace NowFrameWork.Assets
{
    /// <summary>
    /// 把资源层接进框架生命周期：注册到 <see cref="ModuleHub"/>，由启动流程初始化。
    ///
    /// 职责边界：
    /// 1. 初始化 Provider（Addressables 的 catalog 加载就在这里）；
    /// 2. 关闭时**检查是否有租约泄漏**，只记 Error，不强行释放（释放由 Scope/池负责）；
    /// 3. 不持有业务资源，也不做引用计数——那些是 AssetService 的职责。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class AssetsModule : IAppModule
    {
        readonly IAssetService _service;
        readonly ILog _log;

        public AssetsModule(IAssetService service, ILog log = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _log = log ?? Logs.Null;
        }

        public string Id => "assets";

        public IReadOnlyList<string> DependsOn => Array.Empty<string>();

        public UniTask InitializeAsync(CancellationToken ct) => _service.InitializeAsync(ct);

        public UniTask StartAsync(CancellationToken ct) => UniTask.CompletedTask;

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            // 走到这里时 Scope 已经关过子级，池也销毁了实例；
            // 还有租约说明有人忘了释放——记下来，但不擅自释放（可能仍在被使用）。
            if (_service.ActiveLeases > 0 && _log.IsEnabled(LogLevel.Error))
            {
                _log.Write(LogLevel.Error, "ASSET",
                    "模块关闭时仍有 " + _service.ActiveLeases + " 个租约未释放（可能存在泄漏）");
            }

            return UniTask.CompletedTask;
        }
    }
}