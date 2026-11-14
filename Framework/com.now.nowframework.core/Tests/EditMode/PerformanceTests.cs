using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace NowFrameWork.Core.Tests
{
    /// <summary>
    /// 框架层性能与 GC 门禁。规则见 docs/06 §4.2。
    ///
    /// 测量方式：GC.GetAllocatedBytesForCurrentThread()——精确到字节的线程本地分配计数，
    /// 编辑器 Mono（unityjit）与编译引用程序集都提供该 API，因此可以写成断言而不是靠人看 Profiler。
    /// 计时用 Stopwatch，阈值刻意放得很松：只用于拦住"数量级退化"，不作为性能基准。
    /// </summary>
    public class PerformanceTests
    {
        sealed class NopModule : IAppModule, ITickable, ILateTickable, IFixedTickable
        {
            public NopModule(string id) { Id = id; }

            public string Id { get; }
            public IReadOnlyList<string> DependsOn => Array.Empty<string>();

            public int Ticks;

            public UniTask InitializeAsync(CancellationToken ct) => UniTask.CompletedTask;
            public UniTask StartAsync(CancellationToken ct) => UniTask.CompletedTask;
            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;

            public void Tick(float deltaTime, float unscaledDeltaTime) => Ticks++;
            public void LateTick() { }
            public void FixedTick(float fixedDeltaTime) { }
        }

        static ModuleHub CreateStarted(int moduleCount, out NopModule[] modules)
        {
            var hub = new ModuleHub();
            modules = new NopModule[moduleCount];
            for (int i = 0; i < moduleCount; i++)
            {
                modules[i] = new NopModule("M" + i);
                hub.Register(modules[i]);
            }

            var awaiter = hub.StartupAsync().GetAwaiter();
            if (!awaiter.IsCompleted) Assert.Fail("Startup 应同步完成");
            awaiter.GetResult();
            return hub;
        }

        // ---- 稳态：每帧路径必须零分配 ----

        [Test]
        public void Tick_SteadyState_AllocatesZeroBytes()
        {
            ModuleHub hub = CreateStarted(16, out _);
            hub.Tick(0f, 0f);   // 预热：首次调用可能触发 JIT/接口派发初始化

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) hub.Tick(0.016f, 0.016f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, "ModuleHub.Tick 在稳态下必须零分配（16 模块 × 10000 帧）");
        }

        [Test]
        public void LateAndFixedTick_SteadyState_AllocateZeroBytes()
        {
            ModuleHub hub = CreateStarted(16, out _);
            hub.LateTick();
            hub.FixedTick(0f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                hub.LateTick();
                hub.FixedTick(0.02f);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, "LateTick/FixedTick 在稳态下必须零分配");
        }

        [Test]
        public void GetAndTryGet_AllocateZeroBytes()
        {
            ModuleHub hub = CreateStarted(8, out NopModule[] modules);
            hub.TryGet<NopModule>(out _);   // 预热

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                hub.TryGet<NopModule>(out NopModule m);
                hub.Get<NopModule>();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, "按类型查询必须零分配");
            Assert.AreSame(modules[0], hub.Get<NopModule>());
        }

        [Test]
        public void TryGetAll_WithPreallocatedBuffer_AllocatesZeroBytes()
        {
            ModuleHub hub = CreateStarted(8, out _);
            var buffer = new List<NopModule>(16);
            hub.TryGetAll(buffer);   // 预热，同时把 List 容量撑到位

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) hub.TryGetAll(buffer);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, "TryGetAll 使用调用方缓冲时不得分配");
            Assert.AreEqual(8, buffer.Count);
        }

        // ---- 一次性成本：预算型断言，防止未来悄悄膨胀 ----

        [Test]
        public void StartupAndShutdown_OneCycle_StaysWithinBudget()
        {
            var hub = new ModuleHub();
            for (int i = 0; i < 16; i++) hub.Register(new NopModule("M" + i));

            long before = GC.GetAllocatedBytesForCurrentThread();
            var a = hub.StartupAsync().GetAwaiter();
            if (!a.IsCompleted) Assert.Fail("Startup 应同步完成");
            a.GetResult();
            var b = hub.ShutdownAsync().GetAwaiter();
            if (!b.IsCompleted) Assert.Fail("Shutdown 应同步完成");
            b.GetResult();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // 预算 16 KB：16 个模块的一次性启停，含排序缓冲首次扩容。若超了说明有意外分配。
            Assert.Less(allocated, 16 * 1024L, "单次 Startup+Shutdown 分配超出预算: " + allocated + " B");
        }

        // ---- 数量级退化保护（阈值很松，不作为基准）----

        [Test]
        public void Tick_40Modules_10000Frames_UnderTimeBudget()
        {
            ModuleHub hub = CreateStarted(40, out NopModule[] modules);
            hub.Tick(0f, 0f);   // 预热
            int ticksBefore = modules[0].Ticks;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++) hub.Tick(0.016f, 0.016f);
            sw.Stop();

            // 40 模块 × 10000 帧 = 40 万次接口调用；实测应在个位数毫秒，给 50 ms 上限防数量级退化
            Assert.Less(sw.ElapsedMilliseconds, 50L, "Tick 明显变慢: " + sw.ElapsedMilliseconds + " ms");
            Assert.AreEqual(ticksBefore + 10000, modules[0].Ticks, "每个模块每帧必须恰好收到一次 Tick");
        }
    }
}