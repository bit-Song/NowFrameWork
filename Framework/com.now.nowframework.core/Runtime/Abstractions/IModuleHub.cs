using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Core
{
    /// <summary>
    /// C01 模块容器契约。
    ///
    /// 线程：所有成员只允许在主线程调用；容器不做内部加锁。
    /// 状态：Startup 失败后容器回到「未启动」状态，允许再次 Startup；
    ///       因此模块的 InitializeAsync 必须可重复执行（或自行处理重复初始化）。
    /// </summary>
    public interface IModuleHub
    {
        /// <summary>注册顺序的只读视图；返回缓存好的包装对象，调用不产生分配。</summary>
        IReadOnlyList<IAppModule> Modules { get; }

        /// <summary>空模块、空 Id、重复 Id、启动后再注册，均立即抛 ModuleRegistrationException。</summary>
        void Register(IAppModule module);

        /// <summary>校验依赖 → 拓扑排序 → 全部 Initialize → 全部 Start；任一步失败逆序清理后再抛出。</summary>
        UniTask StartupAsync(CancellationToken ct = default);

        /// <summary>
        /// 逆序关闭已启动模块；单个模块失败不影响其余模块。
        /// 清理阶段**不接受取消**：半关闭状态比清理慢更糟，因此 ct 只用于接口对称。
        /// </summary>
        UniTask ShutdownAsync(CancellationToken ct = default);

        /// <summary>按初始化顺序分发给实现 ITickable 的模块；每帧调用不产生分配。</summary>
        void Tick(float deltaTime, float unscaledDeltaTime);

        void LateTick();

        void FixedTick(float fixedDeltaTime);

        /// <summary>找不到时抛 InvalidOperationException。</summary>
        T Get<T>() where T : class;

        /// <summary>多实现时返回**最先注册**的那个；不确定时应改用 TryGetAll。</summary>
        bool TryGet<T>(out T module) where T : class;

        /// <summary>
        /// 取出全部匹配模块，写入调用方提供的缓冲（先 Clear），避免内部每次分配。
        /// 返回写入数量；顺序与注册顺序一致。
        /// </summary>
        int TryGetAll<T>(List<T> buffer) where T : class;
    }
}