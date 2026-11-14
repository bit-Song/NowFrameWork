using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NowFrameWork.Core
{
    /// <summary>
    /// C01 模块容器：显式依赖、拓扑排序、失败清理、逆序关闭。
    /// 不做反射扫描：启动顺序必须能从 Register 的调用顺序读出来。
    /// </summary>
    public sealed class ModuleHub : IModuleHub
    {
        readonly List<IAppModule> _modules = new List<IAppModule>();
        readonly Dictionary<string, IAppModule> _byId = new Dictionary<string, IAppModule>(StringComparer.Ordinal);
        readonly List<IAppModule> _started = new List<IAppModule>();
        bool _hasStarted;

        public IReadOnlyList<IAppModule> Modules => _modules;

        public void Register(IAppModule module)
        {
            if (module == null) throw new ModuleRegistrationException("Register 收到 null 模块");
            if (string.IsNullOrEmpty(module.Id)) throw new ModuleRegistrationException("模块 Id 不能为空: " + module.GetType().Name);
            if (_hasStarted) throw new ModuleRegistrationException("Startup 完成后不允许再注册: " + module.Id);
            if (_byId.ContainsKey(module.Id)) throw new ModuleRegistrationException("模块 Id 重复: " + module.Id);

            _modules.Add(module);
            _byId.Add(module.Id, module);
        }

        public async UniTask StartupAsync(CancellationToken ct = default)
        {
            if (_hasStarted) throw new InvalidOperationException("ModuleHub 已经 Startup 过");

            // 依赖图先算完：缺失/循环要暴露在「任何模块被初始化」之前
            IReadOnlyList<IAppModule> order = SortByDependencies();

            try
            {
                for (int i = 0; i < order.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await order[i].InitializeAsync(ct);
                    _started.Add(order[i]);   // 初始化成功即登记，后续失败也要被清理
                }

                for (int i = 0; i < order.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await order[i].StartAsync(ct);
                }

                _hasStarted = true;
            }
            catch
            {
                await CleanupStartedAsync();
                throw;
            }
        }

        public async UniTask ShutdownAsync(CancellationToken ct = default)
        {
            await CleanupStartedAsync(ct);
            _hasStarted = false;
        }

        async UniTask CleanupStartedAsync(CancellationToken ct = default)
        {
            for (int i = _started.Count - 1; i >= 0; i--)
            {
                IAppModule module = _started[i];
                try
                {
                    // 清理不能被取消打断，否则会留下半关闭状态
                    await module.ShutdownAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    // TODO(C03): 接入日志后记录模块 Id 与异常；清理异常不阻断后续模块
                }
            }
            _started.Clear();
        }

        /// <summary>深度优先拓扑排序，同时检出缺失依赖与循环依赖。</summary>
        IReadOnlyList<IAppModule> SortByDependencies()
        {
            var order = new List<IAppModule>(_modules.Count);
            var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 0 未访问 / 1 访问中 / 2 完成
            var path = new List<string>();

            void Visit(IAppModule module)
            {
                state.TryGetValue(module.Id, out int s);
                if (s == 2) return;
                if (s == 1) throw new ModuleDependencyException("循环依赖: " + string.Join(" -> ", path) + " -> " + module.Id);

                state[module.Id] = 1;
                path.Add(module.Id);

                IReadOnlyList<string> deps = module.DependsOn;
                if (deps != null)
                {
                    for (int i = 0; i < deps.Count; i++)
                    {
                        string depId = deps[i];
                        if (!_byId.TryGetValue(depId, out IAppModule dep))
                            throw new ModuleDependencyException("模块 " + module.Id + " 依赖的 " + depId + " 未注册；链路: " + string.Join(" -> ", path) + " -> " + depId);
                        Visit(dep);
                    }
                }

                path.RemoveAt(path.Count - 1);
                state[module.Id] = 2;
                order.Add(module);
            }

            for (int i = 0; i < _modules.Count; i++) Visit(_modules[i]);
            return order;
        }

        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            for (int i = 0; i < _started.Count; i++)
                if (_started[i] is ITickable t) t.Tick(deltaTime, unscaledDeltaTime);
        }

        public void LateTick()
        {
            for (int i = 0; i < _started.Count; i++)
                if (_started[i] is ILateTickable t) t.LateTick();
        }

        public void FixedTick(float fixedDeltaTime)
        {
            for (int i = 0; i < _started.Count; i++)
                if (_started[i] is IFixedTickable t) t.FixedTick(fixedDeltaTime);
        }

        public T Get<T>() where T : class
        {
            if (TryGet<T>(out T module)) return module;
            throw new InvalidOperationException("模块未注册: " + typeof(T).Name);
        }

        public bool TryGet<T>(out T module) where T : class
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i] is T found)
                {
                    module = found;
                    return true;
                }
            }
            module = null;
            return false;
        }
    }
}