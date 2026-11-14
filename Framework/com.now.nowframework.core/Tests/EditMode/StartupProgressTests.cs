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
    /// <summary>C02 启动进度：阶段、归一化进度、失败定位。</summary>
    public class StartupProgressTests
    {
        sealed class Collector : IStartupProgress
        {
            public readonly List<string> Phases = new List<string>();
            public readonly List<float> Values = new List<float>();
            public string FailedPhase;
            public string FailedModuleId;

            public void Phase(string name) => Phases.Add(name);
            public void Progress(float value01) => Values.Add(value01);

            public void Failed(string phase, string moduleId)
            {
                FailedPhase = phase;
                FailedModuleId = moduleId;
            }
        }

        sealed class TestModule : IAppModule
        {
            readonly string[] _deps;

            public TestModule(string id, params string[] deps)
            {
                Id = id;
                _deps = deps;
            }

            public string Id { get; }
            public IReadOnlyList<string> DependsOn => _deps;
            public bool ThrowOnInitialize { get; set; }
            public bool ThrowOnStart { get; set; }

            public UniTask InitializeAsync(CancellationToken ct)
            {
                if (ThrowOnInitialize) throw new InvalidOperationException("boom-init:" + Id);
                return UniTask.CompletedTask;
            }

            public UniTask StartAsync(CancellationToken ct)
            {
                if (ThrowOnStart) throw new InvalidOperationException("boom-start:" + Id);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
        }

        static void RunSync(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, "测试假设：C02 的启动是同步完成的");
            awaiter.GetResult();
        }

        [Test]
        public void Startup_ReportsPhasesAndMonotonicProgress()
        {
            var hub = new ModuleHub();
            hub.Register(new TestModule("A"));
            hub.Register(new TestModule("B", "A"));
            var collector = new Collector();

            RunSync(hub.StartupAsync(default, collector));

            CollectionAssert.AreEqual(new[] { "Initialize", "Start" }, collector.Phases);
            Assert.AreEqual(1f, collector.Values[collector.Values.Count - 1], "结束进度必须是 1");

            for (int i = 1; i < collector.Values.Count; i++)
                Assert.LessOrEqual(collector.Values[i - 1], collector.Values[i], "进度必须单调不减");
        }

        [Test]
        public void Startup_InitializeFailure_ReportsPhaseAndModule()
        {
            var hub = new ModuleHub();
            hub.Register(new TestModule("A"));
            hub.Register(new TestModule("B", "A") { ThrowOnInitialize = true });
            var collector = new Collector();

            Assert.Throws<InvalidOperationException>(() => RunSync(hub.StartupAsync(default, collector)));

            Assert.AreEqual("Initialize", collector.FailedPhase);
            Assert.AreEqual("B", collector.FailedModuleId, "失败必须能定位到具体模块");
        }

        [Test]
        public void Startup_StartFailure_ReportsStartPhase()
        {
            var hub = new ModuleHub();
            hub.Register(new TestModule("A") { ThrowOnStart = true });
            var collector = new Collector();

            Assert.Throws<InvalidOperationException>(() => RunSync(hub.StartupAsync(default, collector)));

            Assert.AreEqual("Start", collector.FailedPhase);
            Assert.AreEqual("A", collector.FailedModuleId);
        }

        [Test]
        public void Startup_WithoutProgress_StillWorks()
        {
            var hub = new ModuleHub();
            hub.Register(new TestModule("A"));

            Assert.DoesNotThrow(() => RunSync(hub.StartupAsync()));
        }

        [Test]
        public void ScaledProgress_MapsIntoRange()
        {
            var collector = new Collector();
            var scaled = new ScaledProgress(collector, 0.5f, 1f);

            scaled.Progress(0f);
            scaled.Progress(0.5f);
            scaled.Progress(1f);
            scaled.Progress(2f);   // 越界应被钳制

            CollectionAssert.AreEqual(new[] { 0.5f, 0.75f, 1f, 1f }, collector.Values);
        }

        [Test]
        public void Startup_EmptyHub_ReportsCompleted()
        {
            var hub = new ModuleHub();
            var collector = new Collector();

            RunSync(hub.StartupAsync(default, collector));

            Assert.AreEqual(1, collector.Values.Count);
            Assert.AreEqual(1f, collector.Values[0]);
        }
    }
}
