using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core.Events;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Pooling;
using NowFrameWork.Core.Threading;
using NowFrameWork.Core.Time;

namespace NowFrameWork.Core.Tests
{
    public class MainThreadGuardTests
    {
        struct Ping { }
        sealed class Module : IAppModule
        {
            public string Id => "thread-probe";
            public IReadOnlyList<string> DependsOn => Array.Empty<string>();
            public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;
            public UniTask StartAsync(CancellationToken ct) => UniTask.CompletedTask;
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        static Exception RejectedOnWorker(Action action)
        {
            Exception error = null;
            var worker = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
            worker.IsBackground = true;
            worker.Start();
            Assert.IsTrue(worker.Join(3000), "工作线程只能执行同步断言测试，不能等待 Unity 主线程");
            Assert.That(error, Is.TypeOf<InvalidOperationException>(), error?.ToString());
            return error;
        }

        [Test]
        public void HostBinding_IsIdempotent_AndWorkerCannotRebind()
        {
            int original = MainThreadGuard.ThreadId;
            MainThreadGuard.BindCurrentThread();
            Assert.IsTrue(MainThreadGuard.IsMainThread);
            RejectedOnWorker(MainThreadGuard.BindCurrentThread);
            Assert.AreEqual(original, MainThreadGuard.ThreadId);
        }

        [Test]
        public void Assert_ReportsOperationAndThreadIds_WithoutBindingWorker()
        {
            int main = MainThreadGuard.ThreadId;
            Exception error = RejectedOnWorker(() => MainThreadGuard.Assert("probe-operation"));
            StringAssert.Contains("probe-operation", error.Message);
            StringAssert.Contains("main=" + main, error.Message);
            StringAssert.Contains("current=", error.Message);
            Assert.AreEqual(main, MainThreadGuard.ThreadId);
        }

        [Test]
        public void Assert_IsConditionalOnEditorOrDevelopment()
        {
            var attributes = (ConditionalAttribute[])Attribute.GetCustomAttributes(
                typeof(MainThreadGuard).GetMethod(nameof(MainThreadGuard.Assert)), typeof(ConditionalAttribute));
            var symbols = new List<string>();
            foreach (var attribute in attributes) symbols.Add(attribute.ConditionString);
            CollectionAssert.AreEquivalent(new[] { "UNITY_EDITOR", "DEVELOPMENT_BUILD" }, symbols);
        }

        [Test]
        public void WorkerConstruction_IsRejectedBeforeServiceUse()
        {
            RejectedOnWorker(() => new ModuleHub());
            RejectedOnWorker(() => new EventBus());
            RejectedOnWorker(() => new ObjectPool<object>(() => new object()));
        }

        [Test]
        public void WorkerRegister_DoesNotModifyHub()
        {
            var hub = new ModuleHub();
            RejectedOnWorker(() => hub.Register(new Module()));
            Assert.AreEqual(0, hub.Modules.Count);
        }

        [Test]
        public void WorkerTick_IsRejected_ThenMainThreadTickStillWorks()
        {
            var hub = new ModuleHub();
            hub.StartupAsync().GetAwaiter().GetResult();
            RejectedOnWorker(() => hub.Tick(0.1f, 0.1f));
            Assert.DoesNotThrow(() => hub.Tick(0.1f, 0.1f));
            hub.ShutdownAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void WorkerPublish_DoesNotInvokeSubscribers()
        {
            var bus = new EventBus();
            int calls = 0;
            void Handler(in Ping _) => calls++;
            using (bus.Subscribe<Ping>(Handler))
            {
                RejectedOnWorker(() => bus.Publish(new Ping()));
                Assert.AreEqual(0, calls);
                bus.Publish(new Ping());
                Assert.AreEqual(1, calls);
            }
        }

        [Test]
        public void WorkerUnsubscribe_DoesNotInvalidateToken()
        {
            var bus = new EventBus();
            int calls = 0;
            void Handler(in Ping _) => calls++;
            var token = bus.Subscribe<Ping>(Handler);
            RejectedOnWorker(token.Dispose);
            Assert.IsFalse(token.IsDisposed);
            bus.Publish(new Ping());
            Assert.AreEqual(1, calls);
            token.Dispose();
        }

        [Test]
        public void WorkerTimerRegistration_DoesNotCreateTimer()
        {
            var timers = new TimerService(new ManualClock());
            RejectedOnWorker(() => timers.Delay(1, () => { }));
            Assert.AreEqual(0, timers.PendingCount);
            timers.Delay(1, () => { });
            Assert.AreEqual(1, timers.PendingCount);
            timers.Clear();
        }

        [Test]
        public void WorkerPoolRentAndDispose_DoNotChangePool()
        {
            var pool = new ObjectPool<object>(() => new object(), prewarm: 1);
            RejectedOnWorker(() => pool.Get());
            RejectedOnWorker(() => pool.RentAsync(default).GetAwaiter().GetResult());
            RejectedOnWorker(() => pool.Dispose(true));
            Assert.IsFalse(pool.IsDisposed);
            Assert.AreEqual(1, pool.InactiveCount);
            Assert.AreEqual(0, pool.ActiveCount);
            pool.Dispose();
        }

        [Test]
        public void WorkerLogging_DoesNotReadClockOrWriteRing()
        {
            int clockReads = 0;
            var logger = new Logger(clock: () => { clockReads++; return 0; });
            RejectedOnWorker(() => logger.Write(LogLevel.Error, "probe", "worker"));
            var entries = new List<LogEntry>();
            logger.DumpRecent(entries);
            Assert.AreEqual(0, clockReads);
            Assert.AreEqual(0, entries.Count);
        }
    }
}
