using System;
using NUnit.Framework;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Input.Tests
{
    /// <summary>动作合并与输入屏蔽。</summary>
    public class ActionInputTests
    {
        sealed class FakeSource : IActionSource
        {
            public int Id;
            public ActionState State;
            public int Reads;

            public bool TryRead(int actionId, out ActionState state)
            {
                Reads++;

                if (actionId != Id) { state = default; return false; }

                state = State;
                return true;
            }
        }

        [SetUp]
        public void SetUp() => MainThreadGuard.BindCurrentThread();

        [Test]
        public void Read_ReturnsSourceState()
        {
            var source = new FakeSource { Id = 1, State = new ActionState(0.75f, true, true) };
            var input = new ActionInput(source);

            ActionState state = input.Read(1);

            Assert.AreEqual(0.75f, state.Value, 0.0001f);
            Assert.IsTrue(state.Pressed);
            Assert.IsTrue(state.Held);
            Assert.IsTrue(input.WasPressed(1));
        }

        [Test]
        public void Read_UnknownAction_ReturnsIdle()
        {
            var input = new ActionInput(new FakeSource { Id = 1 });

            ActionState state = input.Read(99);

            Assert.IsTrue(state.IsIdle);
        }

        [Test]
        public void Read_FirstNonIdleSourceWins()
        {
            var idle = new FakeSource { Id = 1, State = ActionState.Idle };
            var active = new FakeSource { Id = 1, State = new ActionState(1f, false, true) };
            var input = new ActionInput(idle, active);

            Assert.AreEqual(1f, input.GetValue(1), 0.0001f);
        }

        [Test]
        public void Suppress_ReturnsIdleUntilAllGatesReleased()
        {
            var source = new FakeSource { Id = 1, State = new ActionState(1f, true, true) };
            var input = new ActionInput(source);

            IDisposable modal = input.Suppress("ui.modal");
            IDisposable pause = input.Suppress("game.paused");

            Assert.IsTrue(input.IsSuppressed);
            Assert.IsTrue(input.Read(1).IsIdle, "屏蔽期间所有动作应为空闲");
            Assert.IsFalse(input.WasPressed(1));

            modal.Dispose();
            Assert.IsTrue(input.IsSuppressed, "还有一层屏蔽时仍应屏蔽");
            Assert.IsTrue(input.Read(1).IsIdle);

            pause.Dispose();
            Assert.IsFalse(input.IsSuppressed);
            Assert.AreEqual(1f, input.GetValue(1), 0.0001f);
        }

        [Test]
        public void Gate_DisposeIsIdempotent()
        {
            var input = new ActionInput(new FakeSource { Id = 1 });

            IDisposable gate = input.Suppress("ui");
            gate.Dispose();
            gate.Dispose();

            Assert.AreEqual(0, input.GateCount);
        }

        [Test]
        public void SetGate_TogglesWithoutDuplicates()
        {
            var source = new FakeSource { Id = 1, State = new ActionState(1f, true, true) };
            var input = new ActionInput(source);

            input.SetGate("ui.modal", true);
            input.SetGate("ui.modal", true);   // 重复开启不应叠加

            Assert.AreEqual(1, input.GateCount);
            Assert.IsTrue(input.Read(1).IsIdle);

            input.SetGate("ui.modal", false);
            input.SetGate("ui.modal", false);  // 重复关闭不应变负

            Assert.AreEqual(0, input.GateCount);
            Assert.AreEqual(1f, input.GetValue(1), 0.0001f);
        }

        [Test]
        public void SetGate_CoexistsWithSuppressTokens()
        {
            var input = new ActionInput(new FakeSource { Id = 1 });

            IDisposable modal = input.Suppress("ui.manual");
            input.SetGate("game.paused", true);
            Assert.AreEqual(2, input.GateCount);

            input.SetGate("game.paused", false);
            Assert.AreEqual(1, input.GateCount, "自动门关闭不应影响手动令牌");

            modal.Dispose();
            Assert.AreEqual(0, input.GateCount);
        }

        [Test]
        public void GetVector_PassesThroughAndClearsWhenSuppressed()
        {
            var source = new FakeSource { Id = 2, State = new ActionState(1f, false, true, new UnityEngine.Vector2(0.5f, -0.5f)) };
            var input = new ActionInput(source);

            UnityEngine.Vector2 value = input.GetVector(2);
            Assert.AreEqual(0.5f, value.x, 0.0001f);
            Assert.AreEqual(-0.5f, value.y, 0.0001f);

            using (input.Suppress("ui.modal"))
                Assert.AreEqual(UnityEngine.Vector2.zero, input.GetVector(2), "屏蔽期间方向必须归零");
        }

        [Test]
        public void Suppress_NullReason_Throws()
        {
            var input = new ActionInput();

            Assert.Throws<ArgumentNullException>(() => input.Suppress(null));
        }
    }
}