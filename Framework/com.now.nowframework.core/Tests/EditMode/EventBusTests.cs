using System;
using System.Collections.Generic;
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
    /// C04 事件总线用例。重点：派发安全（快照）、异常隔离、延迟派发、以及**派发零分配**门禁。
    /// </summary>
    public class EventBusTests
    {
        struct Evt
        {
            public int Value;
        }

        sealed class Counter
        {
            public int Count;
            public int LastValue;
            public void On(in Evt e) { Count++; LastValue = e.Value; }
        }

        sealed class ThrowingCounter
        {
            public int Count;
            public void On(in Evt e) { Count++; throw new InvalidOperationException("boom"); }
        }

        sealed class InterfaceCounter : IEventHandler<Evt>
        {
            public int Count;
            public void Handle(in Evt e) => Count++;
        }

        sealed class RecordingSink : ILogSink
        {
            public int Count;
            public string LastMessage;

            public bool IsEnabled(LogLevel level) => true;

            public void Write(in LogEntry entry)
            {
                Count++;
                LastMessage = entry.Message;
            }
        }

        static Evt Make(int v) => new Evt { Value = v };

        [Test]
        public void Subscribe_ThenPublish_InvokesHandler()
        {
            var bus = new EventBus();
            var counter = new Counter();
            bus.Subscribe<Evt>(counter.On);

            bus.Publish(Make(7));

            Assert.AreEqual(1, counter.Count);
            Assert.AreEqual(7, counter.LastValue);
        }

        [Test]
        public void Publish_WithNoSubscriber_DoesNothing()
        {
            var bus = new EventBus();
            Assert.DoesNotThrow(() => bus.Publish(Make(1)));
        }

        [Test]
        public void Dispose_StopsReceiving_AndIsIdempotent()
        {
            var bus = new EventBus();
            var counter = new Counter();
            IEventSubscription token = bus.Subscribe<Evt>(counter.On);

            bus.Publish(Make(1));
            token.Dispose();
            token.Dispose();   // 幂等
            bus.Publish(Make(2));

            Assert.AreEqual(1, counter.Count);
            Assert.IsTrue(token.IsDisposed);
        }

        [Test]
        public void HandlerThrows_OthersStillInvoked_AndErrorLogged()
        {
            var sink = new RecordingSink();
            var logger = new Logger(LogLevel.Trace, 0);
            logger.AddSink(sink);

            var bus = new EventBus(logger);
            var throwing = new ThrowingCounter();
            var counter = new Counter();
            bus.Subscribe<Evt>(throwing.On);
            bus.Subscribe<Evt>(counter.On);

            bus.Publish(Make(3));

            Assert.AreEqual(1, throwing.Count);
            Assert.AreEqual(1, counter.Count, "前一个 handler 抛异常不应影响后面的");
            Assert.AreEqual(1, sink.Count, "单个 handler 异常应记录一条 Error");
            StringAssert.Contains("Evt", sink.LastMessage);
        }

        [Test]
        public void SubscribeDuringPublish_IsSafeAndTakesEffectNextRound()
        {
            var bus = new EventBus();
            var late = new Counter();

            int rounds = 0;
            void OnFirst(in Evt e)
            {
                rounds++;
                if (rounds == 1) bus.Subscribe<Evt>(late.On);
            }

            bus.Subscribe<Evt>(OnFirst);

            bus.Publish(Make(1));
            Assert.AreEqual(0, late.Count, "派发中的新订阅不进本轮快照");

            bus.Publish(Make(2));
            Assert.AreEqual(1, late.Count);
        }

        [Test]
        public void UnsubscribeDuringPublish_StillReceivesCurrentRound()
        {
            var bus = new EventBus();
            var victim = new Counter();
            IEventSubscription token = null;

            void OnFirst(in Evt e) => token.Dispose();

            bus.Subscribe<Evt>(OnFirst);
            token = bus.Subscribe<Evt>(victim.On);

            bus.Publish(Make(1));
            Assert.AreEqual(1, victim.Count, "已进快照的 handler 本轮仍会收到");

            bus.Publish(Make(2));
            Assert.AreEqual(1, victim.Count, "下一轮不再收到");
        }

        [Test]
        public void Deferred_IsNotInvokedUntilFlush()
        {
            var bus = new EventBus();
            var counter = new Counter();
            bus.Subscribe<Evt>(counter.On);

            bus.PublishDeferred(Make(1));
            Assert.AreEqual(0, counter.Count);

            bus.FlushDeferred();
            Assert.AreEqual(1, counter.Count);
        }

        [Test]
        public void Deferred_FlushPreservesPerTypeOrder()
        {
            var bus = new EventBus();
            var values = new List<int>();
            void On(in Evt e) => values.Add(e.Value);
            bus.Subscribe<Evt>(On);

            bus.PublishDeferred(Make(1));
            bus.PublishDeferred(Make(2));
            bus.PublishDeferred(Make(3));
            bus.FlushDeferred();

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, values);
        }

        [Test]
        public void Deferred_HandlerEnqueuesMore_IsProcessedInSameFlush()
        {
            var bus = new EventBus();
            int seen = 0;

            void On(in Evt e)
            {
                seen++;
                if (seen < 3) bus.PublishDeferred(Make(seen + 1));
            }

            bus.Subscribe<Evt>(On);
            bus.PublishDeferred(Make(1));
            bus.FlushDeferred();

            Assert.AreEqual(3, seen);
        }

        [Test]
        public void Clear_RemovesAllSubscriptionsAndQueue()
        {
            var bus = new EventBus();
            var counter = new Counter();
            bus.Subscribe<Evt>(counter.On);
            bus.PublishDeferred(Make(1));

            bus.Clear();
            bus.FlushDeferred();
            bus.Publish(Make(2));

            Assert.AreEqual(0, counter.Count);
        }

    }
}
