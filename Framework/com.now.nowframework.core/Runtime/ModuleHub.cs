using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Startup;

namespace NowFrameWork.Core
{
    /// <summary>
    /// C01 模块容器：显式依赖、拓扑排序、失败清理、逆序关闭。
    /// 不做反射扫描：启动顺序必须能从 Register 的调用顺序读出来。
    ///
    /// 性能约定：
    /// - 每帧路径（Tick/LateTick/FixedTick）不产生分配：帧回调在 Startup 时缓存到三个列表；
    /// - Modules 返回构造期缓存的只读包装，调用不分配；
    /// - 拓扑排序复用三个临时缓冲，不捕获闭包；
    /// - Startup/Shutdown 只在启动/退出时各执行一次，允许少量分配（异常消息、字符串拼接）。
    ///
    /// 线程：只允许主线程调用，内部不加锁。
    /// </summary>
    public sealed class ModuleHub : IModuleHub
    {
        readonly List<IAppModule> _modules = new List<IAppModule>(8);
        readonly Dictionary<string, IAppModule> _byId = new Dictionary<string, IAppModule>(8, StringComparer.Ordinal);
        readonly IReadOnlyList<IAppModule> _modulesView;

        /// <summary>已成功初始化的模块，按初始化顺序；关闭时倒序遍历。</summary>
        readonly List<IAppModule> _started = new List<IAppModule>(8);

        /// <summary>帧回调缓存：Startup 成功后一次性填好，避免每帧对每个模块做接口检查。</summary>
        readonly List<ITickable> _tickables = new List<ITickable>(8);
        readonly List<ILateTickable> _lateTickables = new List<ILateTickable>(8);
        readonly List<IFixedTickable> _fixedTickables = new List<IFixedTickable>(8);

        /// <summary>拓扑排序的临时缓冲，复用以免每次 Startup 重新分配。</summary>
        readonly Dictionary<string, int> _sortState = new Dictionary<string, int>(8, StringComparer.Ordinal);   // 0 未访问 / 1 访问中 / 2 完成
        readonly List<string> _sortPath = new List<string>(8);
        readonly List<IAppModule> _sortOrder = new List<IAppModule>(8);

        bool _hasStarted;

        readonly ILog _log;

        const string C01Tag = "C01";

        /// <param name="log">可选日志；为空时使用 <see cref="Logs.Null"/>，不产生任何开销。</param>
        public ModuleHub(ILog log = null)
        {
            _log = log ?? Logs.Null;
            _modulesView = _modules.AsReadOnly();   // 构造一次，之后每次访问都复用
        }

        public IReadOnlyList<IAppModule> Modules => _modulesView;

        public void Register(IAppModule module)
        {
            if (module == null) throw new ModuleRegistrationException("Register 收到 null 模块");
            if (string.IsNullOrEmpty(module.Id)) throw new ModuleRegistrationException("模块 Id 不能为空: " + module.GetType().Name);
            if (_hasStarted) throw new ModuleRegistrationException("Startup 完成后不允许再注册: " + module.Id);
            if (_byId.ContainsKey(module.Id)) throw new ModuleRegistrationException("模块 Id 重复: " + module.Id);

            _modules.Add(module);
            _byId.Add(module.Id, module);
        }

        public UniTask StartupAsync(CancellationToken ct = default) => StartupAsync(ct, null);

        /// <summary>
        /// 带进度上报的启动。进度按「Initialize 占 0–0.5、Start 占 0.5–1」划分，
        /// 失败时回调 <see cref="IStartupProgress.Failed"/> 并带上阶段名与模块 Id。
        /// </summary>
        public async UniTask StartupAsync(CancellationToken ct, IStartupProgress progress)
        {
            if (_hasStarted) throw new InvalidOperationException("ModuleHub 已经 Startup 过");

            // 依赖图先算完：缺失/循环必须暴露在「任何模块被初始化」之前
            IReadOnlyList<IAppModule> order = SortByDependencies();

            string phase = "Initialize";
            string currentModule = null;
            float count = order.Count;

            try
            {
                progress?.Phase(phase);

                for (int i = 0; i < order.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    currentModule = order[i].Id;
                    progress?.Progress(count == 0f ? 0f : i / count * 0.5f);

                    await order[i].InitializeAsync(ct);
                    _started.Add(order[i]);   // 初始化成功即登记，后续失败也要被清理
                }

                phase = "Start";
                progress?.Phase(phase);

                for (int i = 0; i < order.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    currentModule = order[i].Id;
                    progress?.Progress(0.5f + (count == 0f ? 0f : i / count * 0.5f));

                    await order[i].StartAsync(ct);
                }

                CacheFrameCallbacks(order);
                _hasStarted = true;

                progress?.Progress(1f);
            }
            catch
            {
                progress?.Failed(phase, currentModule);
                await CleanupStartedAsync();
                throw;
            }
            finally
            {
                // 排序缓冲用完即清，避免继续持有模块引用
                _sortOrder.Clear();
                _sortState.Clear();
                _sortPath.Clear();
            }
        }

        public async UniTask ShutdownAsync(CancellationToken ct = default)
        {
            // ct 不参与清理：清理到一半比清理慢更糟。参数保留仅为与接口对称。
            await CleanupStartedAsync();
            _hasStarted = false;
        }

        async UniTask CleanupStartedAsync()
        {
            for (int i = _started.Count - 1; i >= 0; i--)
            {
                try
                {
                    // 每个模块的清理都用 None：取消只该影响业务，不该打断关闭流程
                    await _started[i].ShutdownAsync(CancellationToken.None);
                }
                catch (Exception e)
                {
                    // 清理异常不阻断后续模块；先判断级别，关闭时不构造字符串（R1/R3）
                    if (_log.IsEnabled(LogLevel.Error))
                        _log.Write(LogLevel.Error, C01Tag,
                            "模块关闭失败: " + _started[i].Id + " -> " + e.GetType().Name + ": " + e.Message);
                }
            }

            _started.Clear();
            _tickables.Clear();
            _lateTickables.Clear();
            _fixedTickables.Clear();
        }

        void CacheFrameCallbacks(IReadOnlyList<IAppModule> order)
        {
            _tickables.Clear();
            _lateTickables.Clear();
            _fixedTickables.Clear();

            for (int i = 0; i < order.Count; i++)
            {
                IAppModule m = order[i];
                if (m is ITickable t) _tickables.Add(t);
                if (m is ILateTickable lt) _lateTickables.Add(lt);
                if (m is IFixedTickable ft) _fixedTickables.Add(ft);
            }
        }

        /// <summary>深度优先拓扑排序，同时检出缺失依赖与循环依赖。结果写入复用的 _sortOrder。</summary>
        IReadOnlyList<IAppModule> SortByDependencies()
        {
            _sortOrder.Clear();
            _sortState.Clear();
            _sortPath.Clear();

            for (int i = 0; i < _modules.Count; i++) Visit(_modules[i]);
            return _sortOrder;
        }

        void Visit(IAppModule module)
        {
            _sortState.TryGetValue(module.Id, out int s);
            if (s == 2) return;
            if (s == 1)
                throw new ModuleDependencyException(ModuleDependencyKind.Cycle,
                    "循环依赖: " + string.Join(" -> ", _sortPath) + " -> " + module.Id);

            _sortState[module.Id] = 1;
            _sortPath.Add(module.Id);

            IReadOnlyList<string> deps = module.DependsOn;
            if (deps != null)
            {
                for (int i = 0; i < deps.Count; i++)
                {
                    string depId = deps[i];
                    if (!_byId.TryGetValue(depId, out IAppModule dep))
                        throw new ModuleDependencyException(ModuleDependencyKind.Missing,
                            "模块 " + module.Id + " 依赖的 " + depId + " 未注册；链路: " + string.Join(" -> ", _sortPath) + " -> " + depId);

                    Visit(dep);
                }
            }

            _sortPath.RemoveAt(_sortPath.Count - 1);
            _sortState[module.Id] = 2;
            _sortOrder.Add(module);
        }

        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            List<ITickable> list = _tickables;
            for (int i = 0; i < list.Count; i++)
            {
                try { list[i].Tick(deltaTime, unscaledDeltaTime); }
                catch (Exception e) { ReportCallbackFailure(list[i] as IAppModule, "Tick", e); }
            }
        }

        public void LateTick()
        {
            List<ILateTickable> list = _lateTickables;
            for (int i = 0; i < list.Count; i++)
            {
                try { list[i].LateTick(); }
                catch (Exception e) { ReportCallbackFailure(list[i] as IAppModule, "LateTick", e); }
            }
        }

        public void FixedTick(float fixedDeltaTime)
        {
            List<IFixedTickable> list = _fixedTickables;
            for (int i = 0; i < list.Count; i++)
            {
                try { list[i].FixedTick(fixedDeltaTime); }
                catch (Exception e) { ReportCallbackFailure(list[i] as IAppModule, "FixedTick", e); }
            }
        }

        /// <summary>
        /// 帧回调的异常必须隔离：一个模块抛异常不能让同一帧里其它模块不被 Tick，
        /// 否则表现是"某个功能忽然整帧停摆"，而且很难定位到具体模块。
        /// </summary>
        void ReportCallbackFailure(IAppModule module, string phase, Exception e)
        {
            if (!_log.IsEnabled(LogLevel.Error)) return;

            string id = module != null ? module.Id : "?";
            _log.Write(LogLevel.Error, C01Tag,
                "模块回调失败(" + phase + "): " + id + " -> " + e.GetType().Name + ": " + e.Message);
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

        public int TryGetAll<T>(List<T> buffer) where T : class
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            for (int i = 0; i < _modules.Count; i++)
                if (_modules[i] is T found) buffer.Add(found);

            return buffer.Count;
        }
    }
}
