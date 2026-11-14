using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace NowFrameWork.Core.Tests
{
    /// <summary>
    /// C01 模块容器的 EditMode 用例。
    ///
    /// 注意（NUnit 3.5 限制）：Unity 自带的 NUnit 是 3.5，既不支持 `async Task` 测试方法，
    /// 也没有 Assert.ThrowsAsync。所以这里统一用 RunSync/ThrowsSync 同步驱动 UniTask，
    /// 并且断言「任务必须已同步完成」——将来某个用例真的引入异步等待时，会明确失败而不是死锁。
    /// </summary>
    public class ModuleHubTests
    {
        sealed class RecordModule : IAppModule, ITickable
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
            public bool ThrowOnShutdown { get; set; }
            public int TickCount { get; private set; }

            public UniTask InitializeAsync(CancellationToken ct)
            {
                _log.Add("init:" + Id);
                if (ThrowOnInitialize) throw new InvalidOperationException("boom-init:" + Id);
                return UniTask.CompletedTask;
            }

            public UniTask StartAsync(CancellationToken ct)
            {
                _log.Add("start:" + Id);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _log.Add("shutdown:" + Id);
                if (ThrowOnShutdown) throw new InvalidOperationException("boom-shutdown:" + Id);
                return UniTask.CompletedTask;
            }

            public void Tick(float deltaTime, float unscaledDeltaTime) => TickCount++;
        }

        static string Join(List<string> log) => string.Join(",", log);

        const string SyncHint = "期望同步完成的 UniTask 变成了异步；该用例需改用 [UnityTest] + 协程等待";

        /// <summary>同步等待一个必须已经完成的 UniTask，失败时把它内部的异常原样抛出。</summary>
        static void RunSync(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, SyncHint);
            awaiter.GetResult();
        }

        /// <summary>同步断言抛出指定异常类型；类型不符或没抛都判失败。</summary>
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
        public void Startup_MissingDependency_ThrowsWithChain()
        {
            var hub = new ModuleHub();
            hub.Register(new RecordModule("B", new List<string>(), "Missing"));

            var ex = ThrowsSync<ModuleDependencyException>(() => hub.StartupAsync());
            StringAssert.Contains("B", ex.Message);
            StringAssert.Contains("Missing", ex.Message);
        }

        [Test]
        public void Startup_CircularDependency_Throws()
        {
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", new List<string>(), "B"));
            hub.Register(new RecordModule("B", new List<string>(), "A"));

            var ex = ThrowsSync<ModuleDependencyException>(() => hub.StartupAsync());
            StringAssert.Contains("循环依赖", ex.Message);
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
        public void Startup_PartialFailure_CleansUpInitializedModules()
        {
            var log = new List<string>();
            var hub = new ModuleHub();
            hub.Register(new RecordModule("A", log));
            hub.Register(new RecordModule("B", log, "A") { ThrowOnInitialize = true });

            ThrowsSync<InvalidOperationException>(() => hub.StartupAsync());
            StringAssert.Contains("shutdown:A", Join(log));
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
        public void Tick_OnlyCallsTickableModules()
        {
            var hub = new ModuleHub();
            var a = new RecordModule("A", new List<string>());
            hub.Register(a);
            RunSync(hub.StartupAsync());

            hub.Tick(0.016f, 0.016f);
            hub.Tick(0.016f, 0.016f);
            Assert.AreEqual(2, a.TickCount);
        }
    }
}