using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core;
using NowFrameWork.Core.Pooling;
using NowFrameWork.Core.Startup;
using NowFrameWork.Core.Time;

namespace NowFrameWork.Assets.Tests
{
    /// <summary>S-01 ~ S-05：Scope 状态机与关闭顺序。</summary>
    public class ScopeTests
    {
        sealed class Counter
        {
            public int FrameWaits;
        }

        sealed class ProbeModule : IAppModule
        {
            readonly List<string> _log;
            readonly PoolService _pools;

            public ProbeModule(string id, List<string> log, PoolService pools = null)
            {
                Id = id;
                _log = log;
                _pools = pools;
            }

            public string Id { get; }
            public IReadOnlyList<string> DependsOn => Array.Empty<string>();
            public bool ThrowOnStart { get; set; }
            public bool UsePoolOnShutdown { get; set; }
            public Action OnInit { get; set; }

            public UniTask InitializeAsync(CancellationToken ct)
            {
                _log.Add("init:" + Id);
                OnInit?.Invoke();
                return UniTask.CompletedTask;
            }

            public UniTask StartAsync(CancellationToken ct)
            {
                if (ThrowOnStart) throw new InvalidOperationException("boom:" + Id);
                _log.Add("start:" + Id);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                if (UsePoolOnShutdown)
                {
                    // 模块关闭时池必须仍可用
                    var pool = _pools.GetOrCreate("probe:" + Id, () => new object());
                    pool.Get();
                }

                _log.Add("shutdown:" + Id);
                return UniTask.CompletedTask;
            }
        }

        sealed class ProgressCollector : IStartupProgress
        {
            public int Phases;
            public float Last;
            public void Phase(string name) => Phases++;
            public void Progress(float value01) => Last = value01;
            public void Failed(string phase, string moduleId) { }
        }

        static Scope CreateScope(List<string> log, Counter counter)
        {
            return new Scope("root", null, new ManualClock(), null, () =>
            {
                counter.FrameWaits++;
                return UniTask.CompletedTask;
            });
        }

        [Test]
        public void CreateChild_BeforeRunning_Throws()
        {
            var log = new List<string>();
            Scope scope = CreateScope(log, new Counter());

            Assert.Throws<InvalidOperationException>(() => scope.CreateChild("child"));
        }

        [Test]
        public void CreateChild_AfterRunning_Works()
        {
            var log = new List<string>();
            Scope scope = CreateScope(log, new Counter());
            scope.StartupAsync().GetAwaiter().GetResult();

            Scope child = scope.CreateChild("child");

            Assert.AreEqual(1, scope.ChildCount);
            Assert.AreSame(scope, child.Parent);
            Assert.AreEqual(ScopeState.Created, child.State);
        }

        [Test]
        public void Startup_MovesToRunning_AndReportsProgress()
        {
            var log = new List<string>();
            Scope scope = CreateScope(log, new Counter());
            scope.Modules.Register(new ProbeModule("A", log));
            var collector = new ProgressCollector();

            scope.StartupAsync(default, collector).GetAwaiter().GetResult();

            Assert.AreEqual(ScopeState.Running, scope.State);
            Assert.Greater(collector.Phases, 0);
            Assert.AreEqual(1f, collector.Last);
        }

        [Test]
        public void StartupFailure_MovesToFaulted()
        {
            var log = new List<string>();
            Scope scope = CreateScope(log, new Counter());
            scope.Modules.Register(new ProbeModule("A", log) { ThrowOnStart = true });

            Assert.Throws<InvalidOperationException>(() => scope.StartupAsync().GetAwaiter().GetResult());
            Assert.AreEqual(ScopeState.Faulted, scope.State);
        }

        [Test]
        public void Startup_Twice_Throws()
        {
            var log = new List<string>();
            Scope scope = CreateScope(log, new Counter());
            scope.StartupAsync().GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => scope.StartupAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void DisposeAsync_ClosesChildrenBeforeParent()
        {
            var log = new List<string>();
            Scope root = CreateScope(log, new Counter());
            root.Modules.Register(new ProbeModule("A", log));
            root.StartupAsync().GetAwaiter().GetResult();

            Scope child = root.CreateChild("child");
            child.Modules.Register(new ProbeModule("B", log));
            child.StartupAsync().GetAwaiter().GetResult();

            log.Clear();
            root.DisposeAsync().GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "shutdown:B", "shutdown:A" }, log);
            Assert.AreEqual(ScopeState.Stopped, root.State);
        }

        [Test]
        public void DisposeAsync_StopsModulesBeforePools()
        {
            var log = new List<string>();
            Scope scope = CreateScope(log, new Counter());
            var module = new ProbeModule("A", log, scope.Pools) { UsePoolOnShutdown = true };
            scope.Modules.Register(module);
            scope.StartupAsync().GetAwaiter().GetResult();

            Assert.DoesNotThrow(() => scope.DisposeAsync().GetAwaiter().GetResult(),
                "模块关闭时池必须仍可用；顺序反了会抛 PoolUsageException");
        }

        [Test]
        public void DisposeAsync_WaitsOneFrame_OncePerScope()
        {
            var log = new List<string>();
            var counter = new Counter();
            Scope root = CreateScope(log, counter);
            root.StartupAsync().GetAwaiter().GetResult();
            Scope child = root.CreateChild("child");
            child.StartupAsync().GetAwaiter().GetResult();

            root.DisposeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(2, counter.FrameWaits, "每个 Scope 关闭后都要等一帧（R-1）");
        }

        [Test]
        public void DisposeAsync_DuringStartup_IsDeferredUntilRunning()
        {
            var log = new List<string>();
            var counter = new Counter();
            Scope scope = CreateScope(log, counter);

            // 模块初始化期间请求释放：不能打断启动，但必须记住
            scope.Modules.Register(new ProbeModule("A", log) { OnInit = () => scope.DisposeAsync().Forget() });

            scope.StartupAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ScopeState.Stopped, scope.State, "启动中请求的释放应在启动完成后自动执行");
            StringAssert.Contains("shutdown:A", string.Join(",", log));
        }

        [Test]
        public void DisposeAsync_IsIdempotent()
        {
            var log = new List<string>();
            var counter = new Counter();
            Scope scope = CreateScope(log, counter);
            scope.StartupAsync().GetAwaiter().GetResult();

            scope.DisposeAsync().GetAwaiter().GetResult();
            scope.DisposeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, counter.FrameWaits, "重复释放不应重复等待/重复清理");
            Assert.AreEqual(ScopeState.Stopped, scope.State);
        }
    }
}