using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace NowFrameWork.Core.Tests
{
    /// <summary>
    /// C05 计时器用例。时间由 ManualClock 手动推进，不依赖帧率；
    /// 末尾两条是零分配门禁（06 §4.2 R1）。
    /// </summary>
    public class TimerServiceTests
    {
        sealed class Counter
        {
            public int Count;
            public void Increment() => Count++;
        }

        [Test]
        public void Delay_FiresOnce_WhenTimeReached()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();

            timers.Delay(1.0, counter.Increment);
            Assert.AreEqual(1, timers.PendingCount);

            clock.Advance(0.5f);
            timers.Update();
            Assert.AreEqual(0, counter.Count, "未到时间不应触发");

            clock.Advance(0.6f);
            timers.Update();
            Assert.AreEqual(1, counter.Count);
            Assert.AreEqual(0, timers.PendingCount, "一次性计时器触发后应释放");

            clock.Advance(5f);
            timers.Update();
            Assert.AreEqual(1, counter.Count, "只触发一次");
        }

        [Test]
        public void Interval_FiresEveryPeriod()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();

            timers.Interval(1.0, counter.Increment);

            for (int i = 0; i < 5; i++)
            {
                clock.Advance(1.0f);
                timers.Update();
            }

            Assert.AreEqual(5, counter.Count);
            Assert.AreEqual(1, timers.PendingCount);
        }

        [Test]
        public void Interval_Immediate_FiresOnFirstUpdate()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();

            timers.Interval(10.0, counter.Increment, immediate: true);
            timers.Update();

            Assert.AreEqual(1, counter.Count);
        }

        [Test]
        public void Interval_BehindManyCycles_FiresOnlyOnce()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();

            timers.Interval(1.0, counter.Increment);
            clock.Advance(5.0f);   // 一口气落后 5 轮
            timers.Update();

            Assert.AreEqual(1, counter.Count, "落后多轮只补一次，防止补帧爆炸");
        }

        [Test]
        public void Cancel_PreventsFiring()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();

            TimerHandle handle = timers.Delay(1.0, counter.Increment);
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(timers.Cancel(in handle));

            clock.Advance(2f);
            timers.Update();

            Assert.AreEqual(0, counter.Count);
            Assert.AreEqual(0, timers.PendingCount);
        }

        [Test]
        public void Cancel_StaleHandle_DoesNotKillReusedSlot()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var first = new Counter();
            var second = new Counter();

            TimerHandle oldHandle = timers.Delay(1.0, first.Increment);
            timers.Cancel(in oldHandle);

            TimerHandle newHandle = timers.Delay(1.0, second.Increment);   // 复用同一槽位
            Assert.IsFalse(timers.Cancel(in oldHandle), "过期句柄必须失效");

            clock.Advance(2f);
            timers.Update();

            Assert.AreEqual(0, first.Count);
            Assert.AreEqual(1, second.Count, "新计时器不能被旧句柄取消");
        }

        [Test]
        public void Cancel_InsideCallback_IsSafe()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            int count = 0;
            TimerHandle self = default;

            void Tick()
            {
                count++;
                timers.Cancel(in self);
            }

            self = timers.Interval(1.0, Tick);

            clock.Advance(1f);
            timers.Update();
            clock.Advance(1f);
            timers.Update();

            Assert.AreEqual(1, count, "回调里取消自己后不应再触发");
            Assert.AreEqual(0, timers.PendingCount);
        }

        [Test]
        public void UnscaledDelay_IgnoresTimeScale()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var scaled = new Counter();
            var unscaled = new Counter();

            timers.Delay(1.0, scaled.Increment);
            timers.DelayUnscaled(1.0, unscaled.Increment);

            clock.TimeScale = 0.5f;   // 游戏时间变慢
            clock.Advance(1.0f);      // 真实 1s，游戏 0.5s
            timers.Update();

            Assert.AreEqual(1, unscaled.Count, "不受缩放的计时器按真实时间触发");
            Assert.AreEqual(0, scaled.Count, "受缩放的计时器还没到");
        }

        [Test]
        public void PausedTimeScale_StopsScaledTimers()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();

            timers.Delay(1.0, counter.Increment);
            clock.TimeScale = 0f;   // 暂停

            for (int i = 0; i < 10; i++)
            {
                clock.Advance(1f);
                timers.Update();
            }

            Assert.AreEqual(0, counter.Count);
        }

        [Test]
        public void Clear_RemovesAllTimers()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();

            timers.Delay(1.0, counter.Increment);
            timers.Interval(1.0, counter.Increment);
            Assert.AreEqual(2, timers.PendingCount);

            timers.Clear();
            clock.Advance(5f);
            timers.Update();

            Assert.AreEqual(0, counter.Count);
            Assert.AreEqual(0, timers.PendingCount);
        }

        [Test]
        public void Slots_AreReused_WithoutGrowing()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock, initialCapacity: 4);
            var counter = new Counter();

            for (int i = 0; i < 100; i++)
            {
                timers.Delay(0.1, counter.Increment);
                clock.Advance(0.2f);
                timers.Update();
            }

            Assert.AreEqual(100, counter.Count);
            Assert.AreEqual(0, timers.PendingCount);
        }

        // ---- 零分配门禁 ----

        [Test]
        public void Update_NoDueTimers_AllocatesZeroBytes()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();
            timers.Delay(1000.0, counter.Increment);

            clock.Advance(0.016f);
            timers.Update();   // 预热

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                clock.Advance(0.016f);
                timers.Update();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, "Update 稳态必须零分配");
        }

        [Test]
        public void Update_WithFiringInterval_AllocatesZeroBytes()
        {
            var clock = new ManualClock();
            var timers = new TimerService(clock);
            var counter = new Counter();
            timers.Interval(0.01, counter.Increment);

            // 每帧推进 0.02（= 2 个周期）：float 的 0.02 与 double 的 0.01 之间的精度差
            // 不会导致"偶尔少触发一次"，计数才是确定的。
            clock.Advance(0.02f);
            timers.Update();   // 预热

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                clock.Advance(0.02f);
                timers.Update();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, "触发路径也必须零分配");
            Assert.AreEqual(10001, counter.Count);
        }
    }
}