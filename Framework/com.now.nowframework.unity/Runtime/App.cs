using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Events;
using NowFrameWork.Core.Time;
using NowFrameWork.Core.Pooling;
using NowFrameWork.Core.Diagnostics;
using NowFrameWork.Core.Startup;

namespace NowFrameWork.Unity
{
    /// <summary>
    /// 框架门面：把 Core 的各个服务装配在一起，并负责帧驱动顺序。
    ///
    /// 驱动顺序（与 Unity 回调一一对应）：
    ///   Tick       → 计时器 → 模块 Tick → 延迟事件派发
    ///   LateTick   → 模块 LateTick
    ///   FixedTick  → 模块 FixedTick
    /// </summary>
    public sealed class App
    {
        readonly UnityClock _clock;

        public App(Logger log)
        {
            Log = log ?? throw new ArgumentNullException(nameof(log));
            _clock = new UnityClock();
            Events = new EventBus(log);
            Timers = new TimerService(_clock, log);
            Pools = new PoolService(_clock, log);
            Hub = new ModuleHub(log);

            // 错误预算挂在日志上：所有 Error 及以上自动聚合，模块无需改动
            Errors = new ErrorBudget(fatalThreshold: 50, maxBuckets: 128, clock: () => _clock.UnscaledNow);
            log.AddSink(Errors);
        }

        public IClock Clock => _clock;
        public Logger Log { get; }
        public EventBus Events { get; }
        public TimerService Timers { get; }

        /// <summary>对象池注册表；资源卸载时用它整池释放。</summary>
        public PoolService Pools { get; }

        /// <summary>错误预算：同类错误聚合计数，超阈值升级为 fatal。</summary>
        public ErrorBudget Errors { get; }
        public ModuleHub Hub { get; }

        /// <summary>创建装配好的 App：控制台出口 + Unity 时钟。</summary>
        public static App Create(LogLevel minLevel = LogLevel.Info, int recentLogCapacity = 256)
        {
            var log = new Logger(minLevel, recentLogCapacity, ReadTime);
            log.AddSink(new UnityConsoleSink());
            return new App(log);
        }

        static double ReadTime() => UnityEngine.Time.timeAsDouble;

        /// <summary>启动全部模块；progress 可为空（无加载界面时）。</summary>
        public UniTask StartupAsync(CancellationToken ct = default, IStartupProgress progress = null)
            => Hub.StartupAsync(ct, progress);

        public UniTask ShutdownAsync()
        {
            Events.Clear();
            Timers.Clear();
            Pools.Dispose();
            return Hub.ShutdownAsync();
        }

        /// <summary>每帧驱动（对应 Update）。</summary>
        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            Timers.Update();
            Pools.Tick();
            Hub.Tick(deltaTime, unscaledDeltaTime);
            Events.FlushDeferred();
        }

        /// <summary>对应 LateUpdate。</summary>
        public void LateTick() => Hub.LateTick();

        /// <summary>对应 FixedUpdate。</summary>
        public void FixedTick(float fixedDeltaTime) => Hub.FixedTick(fixedDeltaTime);
    }
}
