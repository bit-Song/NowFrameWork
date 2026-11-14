using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Events;
using NowFrameWork.Core.Time;
using NowFrameWork.Core.Pooling;
using NowFrameWork.Core.Diagnostics;
using NowFrameWork.Core.Startup;

namespace NowFrameWork.Core.Tests
{
    /// <summary>
    /// C01 模块容器的 EditMode 用例。
    ///
    /// NUnit 3.5 限制：Unity 自带 NUnit 不支持 `async Task` 测试方法，也没有 Assert.ThrowsAsync，
    /// 因此统一用 RunSync/ThrowsSync 同步驱动，并断言任务必须已同步完成。
    /// </summary>
    public class ModuleHubTests
    {
        sealed class RecordModule : IAppModule, ITickable, ILateTickable, IFixedTickable
        {
            readonly List<string> _log;
            readonly string[] _deps;

            public RecordModule(string id, List<string> log, params string[] deps)
            {
                Id = id;
                _log = log;
                _deps = deps;
            }

            public string Id { get; }
            public IReadOnlyList<string> DependsOn => _deps;

            public bool ThrowOnInitialize { get; set; }
            public bool ThrowOnStart { get; set; }
            public bool ThrowOnShutdown { get; set; }
            public bool ThrowOnTick { get; set; }

            public int TickCount { get; private set; }
            public int LateTickCount { get; private set; }
            public int FixedTickCount { get; private set; }

            public UniTask InitializeAsync(CancellationToken ct)
            {
                _log.Add("init:" + Id);
                if (ThrowOnInitialize) throw new InvalidOperationException("boom-init:" + Id);
                return UniTask.CompletedTask;
            }

            public UniTask StartAsync(CancellationToken ct)
            {
                _log.Add("start:" + Id);
                if (ThrowOnStart) throw new InvalidOperationException("boom-start:" + Id);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _log.Add("shutdown:" + Id);
                if (ThrowOnShutdown) throw new InvalidOperationException("boom-shutdown:" + Id);
                return UniTask.CompletedTask;
            }

            public void Tick(float deltaTime, float unscaledDeltaTime)
            {
                if (ThrowOnTick) throw new InvalidOperationException("boom-tick:" + Id);
                TickCount++;
            }
            public void LateTick() => LateTickCount++;
            public void FixedTick(float fixedDeltaTime) => FixedTickCount++;
        }

        static string Join(List<string> log) => string.Join(",", log);

        const string SyncHint = "期望同步完成的 UniTask 变成了异步；该用例需改用 [UnityTest] + 协程等待";

        static void RunSync(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, SyncHint);
            awaiter.GetResult();
        }

        static T ThrowsSync<T>(Func<UniTask> action) where T : Exception
        {
            var awaiter = action().GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, SyncHint);

            try
            {
                awaiter.GetResult();
            }
            catch (T expected)
            {
                return expected;
            }
            catch (Exception other)
            {
                Assert.Fail("期望 " + typeof(T).Name + "，实际抛出 " + other.GetType().Name + ": " + other.Message);
            }

            Assert.Fail("期望抛出 " + typeof(T).Name + "，但没有异常");
            return null;
        }

        [Test]
        public void Startup_InitializesInDependencyOrder()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("B", log, "A"));   // 故意先注册依赖方
            hub.Register(new RecordModule("A", log));

            RunSync(hub.StartupAsync());

            Assert.AreEqual("init:A,init:B,start:A,start:B", Join(log));
        }

        [Test]
        public void Startup_MissingDependency_ThrowsAndInitializesNothing()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("B", log, "Missing"));

            var ex = ThrowsSync<ModuleDependencyException>(() => hub.StartupAsync());

            Assert.AreEqual(ModuleDependencyKind.Missing, ex.Kind);
            StringAssert.Contains("B", ex.Message);
            StringAssert.Contains("Missing", ex.Message);
            Assert.IsEmpty(log, "依赖校验必须发生在任何模块初始化之前");
        }

        [Test]
        public void Startup_CircularDependency_Throws()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log, "B"));
            hub.Register(new RecordModule("B", log, "A"));

            var ex = ThrowsSync<ModuleDependencyException>(() => hub.StartupAsync());

            Assert.AreEqual(ModuleDependencyKind.Cycle, ex.Kind);
            StringAssert.Contains("循环依赖", ex.Message);
            Assert.IsEmpty(log);
        }

        [Test]
        public void Register_DuplicateId_Throws()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log));

            Assert.Throws<ModuleRegistrationException>(() => hub.Register(new RecordModule("A", log)));
        }

        [Test]
        public void Shutdown_RunsInReverseOrder()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log));
            hub.Register(new RecordModule("B", log, "A"));

            RunSync(hub.StartupAsync());
            log.Clear();
            RunSync(hub.ShutdownAsync());

            Assert.AreEqual("shutdown:B,shutdown:A", Join(log));
        }

        [Test]
        public void Startup_InitializeThrows_CleansUpEarlierModules()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log));
            hub.Register(new RecordModule("B", log, "A") { ThrowOnInitialize = true });

            ThrowsSync<InvalidOperationException>(() => hub.StartupAsync());

            StringAssert.Contains("shutdown:A", Join(log));
        }

        [Test]
        public void Startup_StartThrows_CleansUpAllInitializedModules()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log));
            hub.Register(new RecordModule("B", log, "A") { ThrowOnStart = true });

            ThrowsSync<InvalidOperationException>(() => hub.StartupAsync());

            // A 与 B 都已完成 Initialize，清理顺序必须逆序
            StringAssert.Contains("shutdown:B,shutdown:A", Join(log));
        }

        [Test]
        public void Startup_CancelledBeforeInit_InitializesNothing()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log));

            ThrowsSync<OperationCanceledException>(() => hub.StartupAsync(new CancellationToken(true)));

            Assert.IsEmpty(log);
        }

        [Test]
        public void Shutdown_OneModuleThrows_OthersStillCleaned()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log));
            hub.Register(new RecordModule("B", log, "A") { ThrowOnShutdown = true });

            RunSync(hub.StartupAsync());
            log.Clear();
            RunSync(hub.ShutdownAsync());   // B 抛异常，A 仍要被关闭

            Assert.AreEqual("shutdown:B,shutdown:A", Join(log));
        }

        [Test]
        public void Get_And_TryGet_Work()
        {
            var hub = new ModuleHub();
            var a = new RecordModule("A", new List<string>());
            hub.Register(a);
            RunSync(hub.StartupAsync());

            Assert.AreSame(a, hub.Get<RecordModule>());
            Assert.IsTrue(hub.TryGet<RecordModule>(out RecordModule found));
            Assert.AreSame(a, found);
            Assert.IsFalse(hub.TryGet<ModuleHub>(out _));
        }

        [Test]
        public void TryGetAll_ReturnsAllMatchesInRegisterOrder()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log));
            hub.Register(new RecordModule("B", log));
            hub.Register(new RecordModule("C", log));
            RunSync(hub.StartupAsync());

            var buffer = new List<IAppModule>(4);
            int count = hub.TryGetAll<IAppModule>(buffer);

            Assert.AreEqual(3, count);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, buffer.ConvertAll(m => m.Id));
        }

        [Test]
        public void Tick_LateTick_FixedTick_OnlyDispatchToMatchingModules()
        {
            var hub = new ModuleHub();
            var a = new RecordModule("A", new List<string>());
            hub.Register(a);
            RunSync(hub.StartupAsync());

            hub.Tick(0.016f, 0.016f);
            hub.LateTick();
            hub.FixedTick(0.02f);
            hub.Tick(0.016f, 0.016f);

            Assert.AreEqual(2, a.TickCount);
            Assert.AreEqual(1, a.LateTickCount);
            Assert.AreEqual(1, a.FixedTickCount);
        }

        [Test]
        public void Tick_AfterShutdown_DoesNothing()
        {
            var hub = new ModuleHub();
            var a = new RecordModule("A", new List<string>());
            hub.Register(a);
            RunSync(hub.StartupAsync());
            RunSync(hub.ShutdownAsync());

            hub.Tick(0.016f, 0.016f);
            hub.LateTick();
            hub.FixedTick(0.02f);

            Assert.AreEqual(0, a.TickCount);
            Assert.AreEqual(0, a.LateTickCount);
            Assert.AreEqual(0, a.FixedTickCount);
        }

        sealed class RecordingSink : ILogSink
        {
            public int Count;
            public LogLevel LastLevel;
            public string LastMessage;

            public bool IsEnabled(LogLevel level) => true;

            public void Write(in LogEntry entry)
            {
                Count++;
                LastLevel = entry.Level;
                LastMessage = entry.Message;
            }
        }

        [Test]
        public void Tick_OneModuleThrows_OtherModulesStillTick()
        {
            var hub = new ModuleHub();
            var bad = new RecordModule("Bad", new List<string>()) { ThrowOnTick = true };
            var good = new RecordModule("Good", new List<string>());
            hub.Register(bad);
            hub.Register(good);
            RunSync(hub.StartupAsync());

            Assert.DoesNotThrow(() => hub.Tick(0.016f, 0.016f), "模块异常必须被隔离，不能中断整帧");
            Assert.AreEqual(1, good.TickCount, "抛异常的模块不应影响后续模块");
        }

        [Test]
        public void Shutdown_ModuleThrows_LogsErrorWithModuleId()
        {
            var log = new List<string>();
            var sink = new RecordingSink();
            var logger = new Logger(LogLevel.Trace, 0);
            logger.AddSink(sink);

            var hub = new ModuleHub(logger);
            hub.Register(new RecordModule("A", log) { ThrowOnShutdown = true });

            RunSync(hub.StartupAsync());
            RunSync(hub.ShutdownAsync());

            Assert.AreEqual(1, sink.Count, "清理失败必须记录一条 Error");
            Assert.AreEqual(LogLevel.Error, sink.LastLevel);
            StringAssert.Contains("A", sink.LastMessage);
        }
    }
}
