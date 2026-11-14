using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Core
{
    /// <summary>
    /// 框架模块契约。依赖必须显式声明；容器按拓扑序初始化，逆序关闭。
    ///
    /// 生命周期保证：
    /// 1. 依赖校验与拓扑排序在**任何模块被初始化之前**完成，失败时不会有模块进入 Init。
    /// 2. 初始化成功的模块，若后续任一步失败，容器会逆序调用其 ShutdownAsync。
    /// 3. ShutdownAsync 必须幂等，且**必须能在从未 Start 的情况下安全调用**
    ///    （例如容器在 Init 阶段失败、或在 Start 之前被取消）。
    /// </summary>
    public interface IAppModule
    {
        /// <summary>唯一标识，建议等于类型名。注册后不得再改变。</summary>
        string Id { get; }

        /// <summary>依赖的模块 Id；容器在初始化任何模块之前校验缺失与循环。</summary>
        IReadOnlyList<string> DependsOn { get; }

        /// <summary>只做构造与注册，禁止重 IO。失败时容器会逆序清理已初始化模块。</summary>
        UniTask InitializeAsync(CancellationToken ct);

        /// <summary>全部 Initialize 完成后，按同一顺序调用。</summary>
        UniTask StartAsync(CancellationToken ct);

        /// <summary>逆序调用；必须幂等、可在未 Start 时安全调用，且不得抛异常。</summary>
        UniTask ShutdownAsync(CancellationToken ct);
    }
}