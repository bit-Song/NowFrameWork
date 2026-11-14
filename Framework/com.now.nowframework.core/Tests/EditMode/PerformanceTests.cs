using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace NowFrameWork.Core.Tests
{
    /// <summary>
    /// 框架层性能门禁。规则见 docs/06 §4.2。
    ///
    /// 注意：本工程无法自动测量托管分配——实测 GC.GetAllocatedBytesForCurrentThread() 恒返回 0（Mono 桩），
    /// 而 GC.GetTotalMemory 只反映保留内存（有空闲段复用），两者都不能当分配计数器用。
    /// 因此零分配只能靠 Profiler（PlayMode / Development Build）人眼验证 + 代码评审守住 R3；
    /// 这里保留的是**计时**门禁：Stopwatch，阈值刻意放得很松，只拦数量级退化。
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

        // ---- 一次性成本：预算型断言，防止未来悄悄膨胀 ----

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
