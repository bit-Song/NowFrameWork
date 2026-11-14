using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Core
{
    /// <summary>
    /// 框架模块契约。依赖必须显式声明；容器按拓扑序初始化，逆序关闭。
    /// </summary>
    public interface IAppModule
    {
        /// <summary>唯一标识，建议等于类型名。</summary>
        string Id { get; }

        /// <summary>依赖的模块 Id；容器在初始化任何模块之前校验缺失与循环。</summary>
        IReadOnlyList<string> DependsOn { get; }

        /// <summary>只做构造与注册，禁止重 IO。失败时容器会清理已初始化模块。</summary>
        UniTask InitializeAsync(CancellationToken ct);

        /// <summary>全部 Initialize 完成后，按同一顺序调用。</summary>
        UniTask StartAsync(CancellationToken ct);

        /// <summary>逆序调用；必须幂等，重复调用不得抛异常。</summary>
        UniTask ShutdownAsync(CancellationToken ct);
    }
}