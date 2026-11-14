using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using NowFrameWork.Core;
using UnityEngine;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Events;
using NowFrameWork.Core.Time;
using NowFrameWork.Core.Pooling;
using NowFrameWork.Core.Diagnostics;
using NowFrameWork.Core.Startup;

namespace NowFrameWork.Unity.Tests
{
    /// <summary>unity 适配包的装配与生命周期用例。</summary>
    public class AppTests
    {
        [Test]
        public void Create_WiresAllServices()
        {
            App app = App.Create(LogLevel.Warn, 0);

            Assert.IsNotNull(app.Clock);
            Assert.IsNotNull(app.Log);
            Assert.IsNotNull(app.Events);
            Assert.IsNotNull(app.Timers);
            Assert.IsNotNull(app.Pools);
            Assert.IsNotNull(app.Errors);
            Assert.IsNotNull(app.Hub);
        }

        [Test]
        public void StartupAndShutdown_WithNoModules_Completes()
        {
            App app = App.Create(LogLevel.Error, 0);

            var startup = app.StartupAsync().GetAwaiter();
            Assert.IsTrue(startup.IsCompleted, "无模块时 Startup 应同步完成");
            startup.GetResult();

            var shutdown = app.ShutdownAsync().GetAwaiter();
            Assert.IsTrue(shutdown.IsCompleted);
            shutdown.GetResult();
        }

        [Test]
        public void Tick_WithNoModules_DoesNotThrow()
        {
            App app = App.Create(LogLevel.Error, 0);
            var startup = app.StartupAsync().GetAwaiter();
            startup.GetResult();

            Assert.DoesNotThrow(() => app.Tick(0.016f, 0.016f));
            Assert.DoesNotThrow(() => app.LateTick());
            Assert.DoesNotThrow(() => app.FixedTick(0.02f));
        }

        sealed class ProgressCollector : IStartupProgress
        {
            public int PhaseCount;
            public float LastProgress = -1f;

            public void Phase(string name) => PhaseCount++;
            public void Progress(float value01) => LastProgress = value01;
            public void Failed(string phase, string moduleId) { }
        }

        [Test]
        public void Startup_ForwardsProgressToReceiver()
        {
            App app = App.Create(LogLevel.Error, 0);
            var collector = new ProgressCollector();

            var awaiter = app.StartupAsync(default, collector).GetAwaiter();
            awaiter.GetResult();

            Assert.Greater(collector.PhaseCount, 0, "App 必须把阶段上报给接收者");
            Assert.AreEqual(1f, collector.LastProgress, "完成时进度必须是 1");
        }

        sealed class ShutdownProbeModule : IAppModule
        {
            readonly PoolService _pools;
            public bool PoolUsableOnShutdown;

            public ShutdownProbeModule(PoolService pools) => _pools = pools;

            public string Id => "ShutdownProbe";
            public IReadOnlyList<string> DependsOn => System.Array.Empty<string>();

            public Cysharp.Threading.Tasks.UniTask InitializeAsync(CancellationToken ct)
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;

            public Cysharp.Threading.Tasks.UniTask StartAsync(CancellationToken ct)
                => Cysharp.Threading.Tasks.UniTask.CompletedTask;

            public Cysharp.Threading.Tasks.UniTask ShutdownAsync(CancellationToken ct)
            {
                // 模块关闭时池必须仍然可用
                var pool = _pools.GetOrCreate("probe", () => new object());
                PoolUsableOnShutdown = pool.Get() != null;
                return Cysharp.Threading.Tasks.UniTask.CompletedTask;
            }
        }

        [Test]
        public void Shutdown_RunsModulesBeforeDisposingPools()
        {
            App app = App.Create(LogLevel.Error, 0);
            var module = new ShutdownProbeModule(app.Pools);
            app.Hub.Register(module);

            app.StartupAsync().GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => app.ShutdownAsync().GetAwaiter().GetResult(),
                "模块关闭时池应仍可用；顺序反了会抛 PoolUsageException");
            Assert.IsTrue(module.PoolUsableOnShutdown);
        }

        [Test]
        public void UnityClock_ReportsNonNegativeTime()
        {
            var clock = new UnityClock();

            Assert.GreaterOrEqual(clock.Now, 0d);
            Assert.GreaterOrEqual(clock.UnscaledNow, 0d);
        }

        [Test]
        public void GameBootstrap_Initialize_CreatesApp_AndIsIdempotent()
        {
            var go = new GameObject("TestBootstrap");
            try
            {
                var bootstrap = go.AddComponent<GameBootstrap>();

                // EditMode 下 AddComponent 不会触发 Awake，所以显式调用
                bootstrap.Initialize();
                App first = bootstrap.App;
                bootstrap.Initialize();   // 幂等

                Assert.IsNotNull(first, "Initialize 必须完成 App 装配");
                Assert.AreSame(first, bootstrap.App, "重复 Initialize 不应重建 App");
                Assert.IsFalse(bootstrap.StartupFailed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
