using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Pooling;
using NowFrameWork.Core.Startup;
using NowFrameWork.Core.Time;

namespace NowFrameWork.Assets
{
    public enum ScopeState
    {
        Created = 0,
        Starting = 1,
        Running = 2,
        Stopping = 3,
        Stopped = 4,
        Faulted = 5,
    }

    /// <summary>
    /// 作用域：拥有自己的模块与池，按固定顺序启动与释放。
    ///
    /// 关闭顺序（与 [13 §4.1] 一致）：
    /// 子 Scope → 模块 → 池 → **等一帧**（R-1）→ 完成。
    ///
    /// 为什么要有"等一帧"：`Object.Destroy` 延迟到帧末，而资源租约释放会立即触发
    /// `AssetBundle.Unload(true)`；同帧释放会留下"对象还在、资源已死"的窗口。
    /// 帧等待可注入，EditMode 测试注入 no-op，避免依赖 PlayerLoop。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class Scope : IDisposable
    {
        readonly List<Scope> _children = new List<Scope>(4);
        readonly IClock _clock;
        readonly ILog _log;
        readonly Func<UniTask> _frameWaiter;

        ScopeState _state = ScopeState.Created;

        /// <summary>启动过程中收到释放请求：等启动结束后立即释放，而不是丢弃。</summary>
        bool _disposeRequested;

        public Scope(string name, Scope parent, IClock clock, ILog log = null, Func<UniTask> frameWaiter = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Scope 名称不能为空", nameof(name));

            Name = name;
            Parent = parent;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? Logs.Null;
            _frameWaiter = frameWaiter ?? DefaultFrameWaiter;

            Modules = new ModuleHub(_log);
            Pools = new PoolService(_clock, _log);
        }

        /// <summary>
        /// 默认等待帧数。Player 实测（[13 §9.3]）：释放最后一个租约后资源在第 2 帧才失效
        /// （Addressables 异步卸载），因此"跨一帧"不够，默认留到 3 帧。
        /// </summary>
        const int DefaultSettleFrames = 3;

        static async UniTask DefaultFrameWaiter()
        {
            for (int i = 0; i < DefaultSettleFrames; i++) await UniTask.NextFrame();
        }

        public string Name { get; }

        public Scope Parent { get; }

        public ScopeState State => _state;

        public ModuleHub Modules { get; }

        public PoolService Pools { get; }

        public int ChildCount => _children.Count;

        /// <summary>只有 Running 状态能派生子 Scope，避免在启动/关闭过程中长出新的生命周期。</summary>
        public Scope CreateChild(string name)
        {
            if (_state != ScopeState.Running)
                throw new InvalidOperationException("只有 Running 状态才能创建子 Scope，当前: " + _state + " (" + Name + ")");

            var child = new Scope(name, this, _clock, _log, _frameWaiter);
            _children.Add(child);
            return child;
        }

        public async UniTask StartupAsync(CancellationToken ct = default, IStartupProgress progress = null)
        {
            if (_state != ScopeState.Created)
                throw new InvalidOperationException("Scope 只能从 Created 启动，当前: " + _state + " (" + Name + ")");

            _state = ScopeState.Starting;

            try
            {
                await Modules.StartupAsync(ct, progress);
                _state = ScopeState.Running;

                // 启动期间有人请求过释放：现在才真正能释放
                if (_disposeRequested)
                {
                    _disposeRequested = false;
                    await DisposeAsync();
                }
            }
            catch
            {
                // 启动失败：模块已由 ModuleHub 逆序清理，这里补上池的清理
                _state = ScopeState.Faulted;
                Pools.Dispose();
                throw;
            }
        }

        /// <param name="purgeAssets">预留：资源层落地后用于强制卸载本 Scope 独占资源。</param>
        public async UniTask DisposeAsync(bool purgeAssets = false)
        {
            // 幂等 + 可重入：Stopping 期间再次调用直接返回，避免重复清理与重复等帧
            if (_state == ScopeState.Stopping || _state == ScopeState.Stopped) return;

            if (_state == ScopeState.Starting)
            {
                // 不能打断启动，但要记住这个请求，启动结束后立即释放
                _disposeRequested = true;
                if (_log.IsEnabled(LogLevel.Debug))
                    _log.Write(LogLevel.Debug, "SCOPE", "启动中收到释放请求，将在启动完成后执行: " + Name);
                return;
            }

            _state = ScopeState.Stopping;

            try
            {
                // 1. 子 Scope 先关（父级释放后子级还在用就晚了）
                for (int i = _children.Count - 1; i >= 0; i--)
                    await _children[i].DisposeAsync(purgeAssets);

                _children.Clear();

                // 2. 模块停止使用资源
                await Modules.ShutdownAsync();

                // 3. 池销毁实例（同步）
                Pools.Dispose();

                // 4. R-1：等一帧，让 Object.Destroy 生效，再允许租约释放
                await _frameWaiter();

                if (purgeAssets && _log.IsEnabled(LogLevel.Debug))
                    _log.Write(LogLevel.Debug, "SCOPE", "purgeAssets 已请求，但资源层尚未接入: " + Name);

                _state = ScopeState.Stopped;
            }
            catch (Exception e)
            {
                // 卡在 Stopping 会让这个 Scope 永远无法再释放；标记为 Faulted 并暴露原因
                _state = ScopeState.Faulted;

                if (_log.IsEnabled(LogLevel.Error))
                    _log.Write(LogLevel.Error, "SCOPE",
                        "释放失败: " + Name + " -> " + e.GetType().Name + ": " + e.Message);
                throw;
            }
        }

        /// <summary>同步兜底：已停止则幂等；否则记 Error 并尽力异步释放（不阻塞主线程）。</summary>
        public void Dispose()
        {
            if (_state == ScopeState.Stopped) return;

            if (_log.IsEnabled(LogLevel.Error))
                _log.Write(LogLevel.Error, "SCOPE",
                    "Scope 被同步释放（应当用 DisposeAsync）: " + Name + "，当前状态 " + _state);

            if (_state == ScopeState.Starting)
            {
                _disposeRequested = true;   // 启动结束后自动释放
                return;
            }

            DisposeAsync().Forget();
        }

        public override string ToString() => "Scope(" + Name + ", " + _state + ")";
    }
}