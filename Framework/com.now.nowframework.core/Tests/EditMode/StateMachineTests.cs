using System;
using NowFrameWork.Core.Fsm;
using NowFrameWork.Core.Threading;
using NUnit.Framework;

namespace NowFrameWork.Core.Tests
{
    public sealed class StateMachineTests
    {
        [SetUp]
        public void SetUp() => MainThreadGuard.BindCurrentThread();

        [Test]
        public void Switch_ExitFailureBlocksRestartUntilCleanupIsRetried()
        {
            var error = new InvalidOperationException("exit failed");
            var active = new Probe { ExitFailure = error };
            var next = new Probe();
            var machine = Create(active);
            machine.AddState("next", next);
            machine.Enter();

            Assert.That(Assert.Throws<InvalidOperationException>(() => machine.ChangeState("next")), Is.SameAs(error));
            Assert.That(active.Exits, Is.EqualTo(1));
            Assert.That(next.Enters, Is.Zero);
            Assert.That(machine.IsFaulted && machine.HasPendingCleanup, Is.True);
            Assert.That(machine.CurrentState, Is.Null);
            Assert.Throws<InvalidOperationException>(() => machine.Restart());

            var retryError = new InvalidOperationException("retry failed");
            active.ExitFailure = retryError;
            Assert.That(Assert.Throws<InvalidOperationException>(machine.Exit), Is.SameAs(retryError));
            Assert.That(machine.LastException, Is.SameAs(error));
            Assert.That(machine.LastCleanupException, Is.SameAs(retryError));
            active.ExitFailure = null;
            machine.Exit();
            machine.Exit();
            Assert.That(active.Exits, Is.EqualTo(3));
            Assert.That(machine.HasPendingCleanup, Is.False);
            machine.Restart();
            Assert.That(active.Enters, Is.EqualTo(2));
        }

        [Test]
        public void Tick_ForcedLocalRuleInterruptsWaitingRequest()
        {
            var active = new Probe { CanExit = false };
            var machine = Create(active);
            machine.AddState("waiting", new Probe());
            machine.AddState("forced", new Probe());
            machine.AddTransition("active", "forced", () => true, force: true);
            machine.Enter();
            machine.ChangeState("waiting");

            Assert.That(machine.PendingTarget, Is.EqualTo("waiting"));
            machine.Tick();
            Assert.That(machine.CurrentStateId, Is.EqualTo("forced"));
            Assert.That(machine.HasPendingTransition, Is.False);
            Assert.That(active.Exits, Is.EqualTo(1));
        }

        [Test]
        public void NestedLifecycle_RootOwnsChildDriveAndRetriesFailedLeafExit()
        {
            var active = new Probe();
            var child = Create(active);
            var root = new StateMachine<int> { Name = "Root" };
            root.AddState(0, child);
            root.SetInitialState(0);
            root.Enter();

            Assert.Throws<InvalidOperationException>(child.Exit);
            Assert.Throws<InvalidOperationException>(child.Tick);
            Assert.Throws<InvalidOperationException>(child.FixedTick);
            root.Tick();
            Assert.That(active.Ticks, Is.EqualTo(1));

            active.ExitFailure = new InvalidOperationException("leaf cleanup failed");
            Assert.That(Assert.Throws<InvalidOperationException>(root.Exit), Is.SameAs(active.ExitFailure));
            Assert.That(root.HasPendingCleanup && child.HasPendingCleanup, Is.True);
            Assert.That(root.FaultPath, Is.EqualTo("Root/0/active"));
            Assert.That(child.FaultPath, Is.EqualTo(root.FaultPath));
            Assert.That(active.Exits, Is.EqualTo(1));
            active.ExitFailure = null;
            root.Exit();
            Assert.That(root.HasPendingCleanup || child.HasPendingCleanup, Is.False);
            Assert.That(active.Exits, Is.EqualTo(2));
        }

        [Test]
        public void ForcedEvent_ReplacesWaitingTransitionAndRetainsFollowingEvent()
        {
            var active = new Probe { CanExit = false };
            var waiting = new Probe();
            var machine = Create(active);
            machine.AddState("waiting", waiting);
            machine.AddState("forced", new Probe());
            machine.AddState("done", new Probe());
            machine.AddEventTransition("active", "force", "forced", force: true);
            machine.AddEventTransition("forced", "next", "done");
            active.TickAction = () =>
            {
                machine.SendEvent("force");
                machine.SendEvent("next");
            };
            machine.Enter();
            machine.ChangeState("waiting");
            Assert.That(machine.PendingTarget, Is.EqualTo("waiting"));

            machine.Tick();

            Assert.That(machine.CurrentStateId, Is.EqualTo("done"));
            Assert.That(waiting.Enters, Is.Zero);
            Assert.That(machine.HasPendingTransition, Is.False);
        }

        [Test]
        public void Tick_ParentRuleWinsBeforeChildRuleIsEvaluated()
        {
            var leaf = new Probe();
            var child = Create(leaf);
            var childTarget = new Probe();
            child.AddState("target", childTarget);
            int childChecks = 0;
            child.AddTransition("active", "target", () => { childChecks++; return true; });

            var root = new StateMachine<int>();
            root.AddState(0, child);
            root.AddState(1, new Probe());
            root.SetInitialState(0);
            root.AddTransition(0, 1, () => true);
            root.Enter();

            root.Tick();

            Assert.That(root.CurrentStateId, Is.EqualTo(1));
            Assert.That(childChecks, Is.Zero);
            Assert.That(childTarget.Enters, Is.Zero);
            Assert.That(leaf.Exits, Is.EqualTo(1));
        }

        [Test]
        public void ReenterChild_RestoresLastStateUntilRootRestartClearsHistory()
        {
            var child = Create(new Probe());
            child.EntryMode = StateEntryMode.LastState;
            child.AddState("second", new Probe());
            var root = new StateMachine<int>();
            root.AddState(0, child);
            root.AddState(1, new Probe());
            root.SetInitialState(0);
            root.Enter();
            child.ChangeState("second");
            root.ChangeState(1);

            root.ChangeState(0);

            Assert.That(child.CurrentStateId, Is.EqualTo("second"));
            Assert.That(child.History[child.History.Count - 1].Reason, Is.EqualTo(StateChangeReason.History));
            root.Restart();
            Assert.That(child.CurrentStateId, Is.EqualTo("active"));
        }

        [Test]
        public void Enter_FailureAfterAcquiringResourceCleansUpBeforeReturning()
        {
            var expected = new InvalidOperationException("enter failed");
            bool resourceHeld = false;
            var active = new Probe
            {
                EnterAction = () => { resourceHeld = true; throw expected; },
                ExitAction = () => resourceHeld = false
            };
            var machine = Create(active);

            Assert.That(Assert.Throws<InvalidOperationException>(machine.Enter), Is.SameAs(expected));
            Assert.That(resourceHeld, Is.False);
            Assert.That(active.Exits, Is.EqualTo(1));
            Assert.That(machine.IsFaulted, Is.True);
            Assert.That(machine.HasPendingCleanup, Is.False);
            Assert.That(machine.CurrentState, Is.Null);
        }

        [Test]
        public void Enter_RecursiveSwitchesStopAtLimitAndClearTheActiveState()
        {
            var active = new Probe();
            var next = new Probe();
            var machine = Create(active);
            machine.AddState("next", next);
            active.EnterAction = () => machine.ChangeState("next");
            next.EnterAction = () => machine.ChangeState("active");

            var error = Assert.Throws<InvalidOperationException>(machine.Enter);

            Assert.That(error.Message, Does.Contain("连续切换超过上限"));
            Assert.That(machine.IsFaulted, Is.True);
            Assert.That(machine.HasPendingTransition, Is.False);
            Assert.That(machine.HasPendingCleanup, Is.False);
            Assert.That(machine.CurrentState, Is.Null);
            Assert.That(active.Exits, Is.EqualTo(active.Enters));
            Assert.That(next.Exits, Is.EqualTo(next.Enters));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Exit_DiagnosticClockFailureDoesNotPreventCleanup(bool traceEnabled)
        {
            var clockError = new InvalidOperationException("clock failed");
            var active = new Probe();
            bool fail = false;
            var machine = Create(active, () => fail ? throw clockError : 10);
            machine.TraceEnabled = traceEnabled;
            machine.Enter();
            fail = true;
            machine.Exit();

            Assert.That(active.Exits, Is.EqualTo(1));
            Assert.That(machine.CurrentState, Is.Null);
            Assert.That(machine.IsFaulted, Is.False);
            Assert.That(machine.LastDiagnosticsException, Is.SameAs(clockError));
        }

        private static StateMachine<string> Create(Probe state, Func<double> clock = null)
        {
            var machine = new StateMachine<string>(clock);
            machine.AddState("active", state);
            machine.SetInitialState("active");
            return machine;
        }

        private sealed class Probe : IState, IStateExitHandler
        {
            public bool CanExit { get; set; } = true;
            public Exception ExitFailure { get; set; }
            public Action EnterAction { get; set; }
            public Action TickAction { get; set; }
            public Action ExitAction { get; set; }
            public int Enters { get; private set; }
            public int Ticks { get; private set; }
            public int Exits { get; private set; }
            public void Enter()
            {
                Enters++;
                EnterAction?.Invoke();
            }

            public void Tick()
            {
                Ticks++;
                TickAction?.Invoke();
            }
            public void FixedTick() { }
            public void OnExitRequested() { }

            public void Exit()
            {
                Exits++;
                if (ExitFailure != null) throw ExitFailure;
                ExitAction?.Invoke();
            }
        }
    }
}
