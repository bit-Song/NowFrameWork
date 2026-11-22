using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Platform;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Input
{
    /// <summary>
    /// 返回导航的输入分发：唯一读返回键的地方。
    ///
    /// 规则：
    /// 1. **每帧最多分发一次**（多个来源同时按下只算一次）；
    /// 2. **分发中不接收新请求**（避免 UI 过渡期连发关掉两层）；
    /// 3. **后台暂停**：应用进入 Background/Quitting 期间不接收，恢复后不补发；
    /// 4. 先问 UI（`RequestBackAsync`），UI 不处理再交给 `IBackFallback`；
    /// 5. 异常只记录并把本次路由标记为 Unhandled，不抛出到 Tick。
    ///
    /// 本模块**不做**动作映射、改键与 UI 焦点导航。
    /// 线程：仅主线程。
    /// </summary>
    public sealed class InputModule : IAppModule, ITickable
    {
        readonly Func<CancellationToken, UniTask<bool>> _tryUiBack;
        readonly IBackFallback _fallback;
        readonly IAppLifecycle _lifecycle;
        readonly ILog _log;
        readonly IBackSource[] _sources;

        bool _ready;
        bool _running;
        bool _paused;
        bool _dispatching;
        bool _shutdown;

        public InputModule(Func<CancellationToken, UniTask<bool>> tryUiBack, IBackFallback fallback = null,
            IAppLifecycle lifecycle = null, ILog log = null, params IBackSource[] sources)
        {
            MainThreadGuard.Assert("InputModule.ctor");

            _tryUiBack = tryUiBack ?? throw new ArgumentNullException(nameof(tryUiBack));
            _fallback = fallback;
            _lifecycle = lifecycle;
            _log = log ?? Logs.Null;
            _sources = sources ?? Array.Empty<IBackSource>();

            for (int i = 0; i < _sources.Length; i++)
                if (_sources[i] == null) throw new ArgumentException("输入源包含 null", nameof(sources));
        }

        public string Id => "input";

        public IReadOnlyList<string> DependsOn => Array.Empty<string>();

        /// <summary>最近一次返回请求的去向。</summary>
        public BackRoute LastRoute { get; private set; }

        /// <summary>累计分发次数（诊断用）。</summary>
        public int BackCount { get; private set; }

        public bool IsPaused => _paused;

        public UniTask InitializeAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("InputModule.InitializeAsync");
            ct.ThrowIfCancellationRequested();

            if (_ready) throw new InvalidOperationException("InputModule 已初始化");

            if (_lifecycle != null)
            {
                _paused = IsBackground(_lifecycle.State);
                _lifecycle.StateChanged += OnStateChanged;
            }

            _ready = true;
            return UniTask.CompletedTask;
        }

        public UniTask StartAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("InputModule.StartAsync");
            ct.ThrowIfCancellationRequested();

            if (!_ready) throw new InvalidOperationException("InputModule 未初始化");
            _running = true;
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            MainThreadGuard.Assert("InputModule.ShutdownAsync");

            if (_shutdown) return UniTask.CompletedTask;
            _shutdown = true;
            _running = false;
            _ready = false;

            if (_lifecycle != null) _lifecycle.StateChanged -= OnStateChanged;

            return UniTask.CompletedTask;
        }

        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (!_running || _paused || _dispatching || _shutdown) return;

            bool pressed = false;

            for (int i = 0; i < _sources.Length; i++)
            {
                if (!_sources[i].TryConsumeBack()) continue;
                pressed = true;
                break;   // 一帧只取一次，避免多个来源各触发一遍
            }

            if (!pressed) return;

            DispatchAsync().Forget();
        }

        async UniTaskVoid DispatchAsync()
        {
            _dispatching = true;
            BackRoute route = BackRoute.Unhandled;

            try
            {
                if (await _tryUiBack(CancellationToken.None))
                {
                    route = BackRoute.Ui;
                }
                else if (_fallback != null && _fallback.OnBack())
                {
                    route = BackRoute.Fallback;
                }
            }
            catch (Exception error)
            {
                if (_log.IsEnabled(LogLevel.Error))
                    _log.Write(LogLevel.Error, "INPUT", "返回分发失败: " + error);

                route = BackRoute.Unhandled;
            }
            finally
            {
                // 先复位再暴露路由：订阅 LastRoute 的观察者可能立即发起下一次按键
                _dispatching = false;
                LastRoute = route;
                BackCount++;
            }
        }

        void OnStateChanged(AppState state)
        {
            // 进入后台后不再接收；恢复时不补发（不排队）
            _paused = IsBackground(state);
        }

        static bool IsBackground(AppState state)
            => state == AppState.Background || state == AppState.Quitting;
    }
}