using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Core
{
    public interface IModuleHub
    {
        IReadOnlyList<IAppModule> Modules { get; }

        /// <summary>空模块、空 Id、重复 Id、启动后再注册，均立即抛 ModuleRegistrationException。</summary>
        void Register(IAppModule module);

        /// <summary>校验依赖 → 拓扑排序 → 全部 Initialize → 全部 Start；任一步失败逆序清理后再抛出。</summary>
        UniTask StartupAsync(CancellationToken ct = default);

        /// <summary>逆序关闭已启动模块；单个模块失败不影响其余模块。</summary>
        UniTask ShutdownAsync(CancellationToken ct = default);

        void Tick(float deltaTime, float unscaledDeltaTime);
        void LateTick();
        void FixedTick(float fixedDeltaTime);

        T Get<T>() where T : class;
        bool TryGet<T>(out T module) where T : class;
    }
}