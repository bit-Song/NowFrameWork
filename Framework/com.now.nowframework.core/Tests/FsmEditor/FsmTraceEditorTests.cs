using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NowFrameWork.Core.Fsm.Editor;
using NowFrameWork.Core.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace NowFrameWork.Core.Fsm.EditorTests
{
    /// <summary>验证轨迹归档、增量采集和实际 IMGUI 时间线绘制。</summary>
    public sealed class FsmTraceEditorTests
    {
        private string _savedSession; // 测试结束后恢复原会话，不覆盖用户已有轨迹。
        private FsmDebuggerWindow _window; // 只关闭本测试创建的窗口。
        private bool _windowShown; // 未显示的 EditorWindow 不能直接调用 Close。

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("观察器窗口绘制需要图形设备。");
        }

        [SetUp]
        public void SetUp()
        {
            MainThreadGuard.BindCurrentThread();
            _window = null; _windowShown = false;
            _savedSession = EditorJsonUtility.ToJson(FsmTraceSession.instance);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_window != null)
                {
                    if (_windowShown) _window.Close();
                    else Object.DestroyImmediate(_window);
                }
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(_savedSession, FsmTraceSession.instance);
                FsmTraceSession.instance.Persist();
            }
        }

        [Test]
        public void Archive_IsBoundedAndKeepsOriginalTimeOrigin()
        {
            FsmTraceSession session = FsmTraceSession.instance;
            session.Begin();
            session.Append(CreateRecords(4500));
            Assert.AreEqual(4096, session.Records.Count);
            Assert.AreEqual(404, session.Trimmed);
            Assert.AreEqual(404, session.Records[0].Time);
            Assert.AreEqual(0, session.TimeOrigin);
        }

        [Test]
        public void Archive_SerializationRetainsStoppedSession()
        {
            FsmTraceSession session = FsmTraceSession.instance;
            session.Begin(); session.Append(CreateRecords(5)); session.Finish();
            string saved = EditorJsonUtility.ToJson(session);
            session.Clear();
            EditorJsonUtility.FromJsonOverwrite(saved, session);
            Assert.IsFalse(session.IsLive);
            Assert.AreEqual(5, session.Records.Count);
            Assert.AreEqual("Idle", session.Records[0].Target);
            Assert.AreEqual("开始移动", session.Records[1].RuleName);
        }

        [Test]
        public void Recorder_CapturesStoppedMachineWithoutDuplicatingRecords()
        {
            FsmTraceSession session = FsmTraceSession.instance;
            session.Begin();
            var fsm = new StateMachine<string> { Name = "EditorCapture", TraceEnabled = true };
            fsm.AddState("Idle", new EmptyState()); fsm.AddState("Move", new EmptyState());
            fsm.SetInitialState("Idle"); fsm.Enter(); fsm.ChangeState("Move"); fsm.Exit();
            FsmTraceRecorder.Capture();
            long id = FsmTraceRecorder.GetSessionId(fsm.DebugId);
            int records = 0;
            bool stopped = false;
            foreach (FsmTraceRecord record in session.Records)
                if (record.MachineId == id) { records++; stopped |= record.Kind == FsmTraceKind.Stopped; }
            Assert.AreEqual(5, records);
            Assert.IsTrue(stopped);
            int count = session.Records.Count;
            FsmTraceRecorder.Capture();
            Assert.AreEqual(count, session.Records.Count);
        }

        [Test]
        public void PausedDisplay_DoesNotStopSessionRecording()
        {
            FsmTraceSession session = FsmTraceSession.instance;
            session.Begin(); session.Append(CreateRecords(3));
            _window = ScriptableObject.CreateInstance<FsmDebuggerWindow>();
            _window.SetPaused(true);
            session.Append(CreateRecords(2));
            Assert.AreEqual(3, _window.DisplayRecords.Count);
            Assert.AreEqual(5, session.Records.Count);
            Assert.IsTrue(session.IsLive);
            _window.SetPaused(false);
            Assert.AreEqual(5, _window.DisplayRecords.Count);
        }

        [Test]
        public void Filters_RespectHierarchyKindAndText()
        {
            FsmTraceSession session = FsmTraceSession.instance;
            var records = CreateRecords(1);
            records.Add(new FsmTraceRecord
            {
                Sequence = 2, MachineId = 2, ParentId = 1, RootId = 1, Depth = 1,
                MachineName = "Combat", State = "Attack", Target = "Idle", Kind = FsmTraceKind.WaitStarted,
                RuleName = "等待收招"
            });
            session.Begin(); session.Append(records); session.Finish();
            _window = ScriptableObject.CreateInstance<FsmDebuggerWindow>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FsmDebuggerWindow).GetField("_selectedMachine", flags).SetValue(_window, 1L);
            typeof(FsmDebuggerWindow).GetMethod("BuildMachineList", flags).Invoke(_window, new object[] { records });
            typeof(FsmDebuggerWindow).GetMethod("FilterRecords", flags).Invoke(_window, new object[] { records });
            var visible = (List<FsmTraceRecord>)typeof(FsmDebuggerWindow).GetField("_visible", flags).GetValue(_window);
            Assert.AreEqual(2, visible.Count);
            typeof(FsmDebuggerWindow).GetField("_includeChildren", flags).SetValue(_window, false);
            typeof(FsmDebuggerWindow).GetMethod("FilterRecords", flags).Invoke(_window, new object[] { records });
            Assert.AreEqual(1, visible.Count);
            typeof(FsmDebuggerWindow).GetField("_includeChildren", flags).SetValue(_window, true);
            typeof(FsmDebuggerWindow).GetField("_kindFilter", flags).SetValue(_window, 2);
            typeof(FsmDebuggerWindow).GetField("_search", flags).SetValue(_window, "attack");
            typeof(FsmDebuggerWindow).GetMethod("FilterRecords", flags).Invoke(_window, new object[] { records });
            Assert.AreEqual(1, visible.Count);
            Assert.AreEqual(FsmTraceKind.WaitStarted, visible[0].Kind);
        }

        [UnityTest, Category("RequiresGraphicsDevice")]
        public IEnumerator Timeline_RendersArchivedRowsAndSelectedMachine()
        {
            RequireGraphicsDevice();
            FsmTraceSession session = FsmTraceSession.instance;
            session.Begin(); session.Append(CreateRecords(50)); session.Finish();
            _window = ScriptableObject.CreateInstance<FsmDebuggerWindow>();
            _window.position = new Rect(50, 50, 1100, 720);
            _window.ShowUtility();
            _windowShown = true;
            typeof(FsmDebuggerWindow).GetField("_selectedMachine", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_window, 1L);
            yield return null;
            _window.SendEvent(new Event { type = EventType.Layout });
            _window.SendEvent(new Event { type = EventType.Repaint });
            yield return null;
            typeof(FsmDebuggerWindow).GetField("_tab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_window, 1);
            _window.SendEvent(new Event { type = EventType.Layout });
            _window.SendEvent(new Event { type = EventType.Repaint });
            yield return null;
            var visible = (List<FsmTraceRecord>)typeof(FsmDebuggerWindow).GetField("_visible", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_window);
            Assert.AreEqual(50, visible.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Cards_KeepRepeatedVisitsAndMatchExitDurations()
        {
            var records = CreateCardRecords();
            records.Add(new FsmTraceRecord { Sequence = 6, MachineId = 1, RootId = 1, Kind = FsmTraceKind.Exit, Time = 3, State = "Idle", Duration = 1 });
            records.Add(new FsmTraceRecord { Sequence = 7, MachineId = 1, RootId = 1, Kind = FsmTraceKind.Enter, Time = 3, State = "Idle", Target = "Idle", Reason = StateChangeReason.Reenter });
            var model = new FsmCardTrail(); model.Build(records);
            Assert.AreEqual(1, model.Lanes.Count);
            Assert.AreEqual(4, model.Lanes[0].Cards.Count);
            Assert.AreEqual(.8, model.Lanes[0].Cards[0].Duration);
            Assert.AreEqual(1.2, model.Lanes[0].Cards[1].Duration);
            Assert.AreEqual(FsmCardLink.Transition, model.Lanes[0].Cards[1].Link);
            Assert.AreNotSame(model.Lanes[0].Cards[2], model.Lanes[0].Cards[3]);
            Assert.AreEqual("Idle", model.Lanes[0].Cards[3].Enter.Target);
        }

        [Test]
        public void Cards_DoNotConnectDifferentMachines()
        {
            var records = CreateCardRecords();
            records.Insert(1, new FsmTraceRecord
            { Sequence = 10, MachineId = 2, ParentId = 1, RootId = 1, Depth = 1, MachineName = "Child", Kind = FsmTraceKind.Enter, Target = "Attack" });
            var model = new FsmCardTrail(); model.Build(records);
            Assert.AreEqual(2, model.Lanes.Count);
            Assert.AreEqual(3, model.Lanes[0].Cards.Count);
            Assert.AreEqual(1, model.Lanes[1].Cards.Count);
            Assert.AreEqual(FsmCardLink.None, model.Lanes[1].Cards[0].Link);
            Assert.AreEqual(FsmCardLink.Transition, model.Lanes[0].Cards[1].Link);
        }

        [Test]
        public void Cards_ShowMissingBoundariesWithoutInventingEdgesOrDurations()
        {
            var records = CreateRecords(2); // 两次进入之间没有 Exit，不能猜测旧状态时长。
            var model = new FsmCardTrail(); model.Build(records);
            Assert.IsFalse(model.Lanes[0].Cards[0].HasExit);
            Assert.IsFalse(model.Lanes[0].Cards[0].IsOpen);
            Assert.AreEqual(FsmCardLink.Missing, model.Lanes[0].Cards[1].Link);
            records = CreateCardRecords();
            records.Insert(2, new FsmTraceRecord { Sequence = 3, MachineId = 1, RootId = 1, Kind = FsmTraceKind.Gap });
            model.Build(records);
            Assert.IsTrue(model.Lanes[0].HasGap);
            Assert.AreEqual(FsmCardLink.Missing, model.Lanes[0].Cards[1].Link);
        }

        [Test]
        public void Cards_AssociateWaitingAndFaultWithTheirOwnVisit()
        {
            var records = CreateCardRecords();
            records.Add(new FsmTraceRecord { Sequence = 6, MachineId = 1, RootId = 1, Kind = FsmTraceKind.WaitStarted, State = "Idle", Target = "Move", RuleName = "wait" });
            var model = new FsmCardTrail(); model.Build(records);
            Assert.IsTrue(model.Lanes[0].Cards[2].IsWaiting);
            Assert.IsFalse(model.Lanes[0].Cards[0].IsWaiting);
            records.Add(new FsmTraceRecord { Sequence = 7, MachineId = 1, RootId = 1, Kind = FsmTraceKind.WaitCancelled, State = "Idle", Target = "Move", Duration = .4 });
            records.Add(new FsmTraceRecord { Sequence = 8, MachineId = 1, RootId = 1, Kind = FsmTraceKind.Fault, State = "Idle", Detail = "broken" });
            model.Build(records);
            Assert.IsFalse(model.Lanes[0].Cards[2].IsWaiting);
            Assert.AreEqual(.4, model.Lanes[0].Cards[2].LastWait.Duration);
            Assert.IsTrue(model.Lanes[0].Cards[2].HasFault);
            Assert.AreEqual("broken", model.Lanes[0].Cards[2].Fault.Detail);
        }

        [Test]
        public void Cards_CurrentHighlightAndElapsedTimeFreezeWithPausedView()
        {
            double now = 0;
            var fsm = new StateMachine<string>(() => now) { TraceEnabled = true };
            fsm.AddState("Idle", new EmptyState()); fsm.AddState("Move", new EmptyState()); fsm.SetInitialState("Idle");
            fsm.Enter(); now = 1; fsm.ChangeState("Move"); now = 2; fsm.ChangeState("Idle"); now = 5;
            var records = new List<FsmTraceRecord>(); fsm.CopyTraceTo(records, 0);
            var model = new FsmCardTrail(); model.Build(records);
            _window = ScriptableObject.CreateInstance<FsmDebuggerWindow>();
            var live = (Dictionary<long, IStateMachineInfo>)typeof(FsmDebuggerWindow).GetField("_liveMachines", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_window);
            live[fsm.DebugId] = fsm;
            FsmStateCard last = model.Lanes[0].Cards[2];
            Assert.IsTrue(_window.IsCurrentCard(last));
            Assert.IsFalse(_window.IsCurrentCard(model.Lanes[0].Cards[0]));
            Assert.That(_window.CardDurationLabel(last), Does.Contain(3d.ToString("F3")));
            _window.SetPaused(true); now = 8;
            Assert.That(_window.CardDurationLabel(last), Does.Contain(3d.ToString("F3")));
            _window.SetPaused(false);
            Assert.That(_window.CardDurationLabel(last), Does.Contain(6d.ToString("F3")));
            fsm.Exit();
            Assert.IsFalse(_window.IsCurrentCard(last));
        }

        [UnityTest, Category("RequiresGraphicsDevice")]
        public IEnumerator Cards_AndArrowButtonsSelectActualTraceRecords()
        {
            RequireGraphicsDevice();
            FsmTraceSession session = FsmTraceSession.instance;
            session.Begin(); session.Append(CreateCardRecords()); session.Finish();
            _window = ScriptableObject.CreateInstance<FsmDebuggerWindow>();
            _window.position = new Rect(50, 50, 1200, 740);
            _window.ShowUtility(); _windowShown = true;
            typeof(FsmDebuggerWindow).GetField("_selectedMachine", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_window, 1L);
            yield return null;
            _window.SendEvent(new Event { type = EventType.Layout });
            _window.SendEvent(new Event { type = EventType.Repaint });
            yield return null;
            Assert.Greater(_window.LastCardBounds.width, 0);
            long cardSequence = _window.LastCardRecord.Sequence;
            Vector2 point = _window.LastCardBounds.center;
            _window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
            _window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
            Assert.AreEqual(cardSequence, typeof(FsmDebuggerWindow).GetField("_selectedSequence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_window));
            _window.SendEvent(new Event { type = EventType.Layout });
            _window.SendEvent(new Event { type = EventType.Repaint });
            Assert.Greater(_window.LastArrowBounds.width, 0);
            long arrowSequence = _window.LastArrowRecord.Sequence;
            typeof(FsmDebuggerWindow).GetField("_selectedSequence", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_window, 0L);
            point = _window.LastArrowBounds.center;
            _window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
            _window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
            Assert.AreEqual(arrowSequence, typeof(FsmDebuggerWindow).GetField("_selectedSequence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_window));
            LogAssert.NoUnexpectedReceived();
        }

        private static List<FsmTraceRecord> CreateCardRecords()
        {
            return new List<FsmTraceRecord>
            {
                new FsmTraceRecord { Sequence = 1, MachineId = 1, RootId = 1, MachineName = "Player", Kind = FsmTraceKind.Enter, Target = "Idle", Time = 0, Reason = StateChangeReason.Initial },
                new FsmTraceRecord { Sequence = 2, MachineId = 1, RootId = 1, MachineName = "Player", Kind = FsmTraceKind.Exit, State = "Idle", Time = .8, Duration = .8 },
                new FsmTraceRecord { Sequence = 3, MachineId = 1, RootId = 1, MachineName = "Player", Kind = FsmTraceKind.Enter, State = "Idle", Target = "Move", Time = .8, Reason = StateChangeReason.LocalTransition, RuleName = "开始移动" },
                new FsmTraceRecord { Sequence = 4, MachineId = 1, RootId = 1, MachineName = "Player", Kind = FsmTraceKind.Exit, State = "Move", Time = 2, Duration = 1.2 },
                new FsmTraceRecord { Sequence = 5, MachineId = 1, RootId = 1, MachineName = "Player", Kind = FsmTraceKind.Enter, State = "Move", Target = "Idle", Time = 2, Reason = StateChangeReason.LocalTransition, RuleName = "停止移动" }
            };
        }

        private static List<FsmTraceRecord> CreateRecords(int count)
        {
            var records = new List<FsmTraceRecord>(count);
            for (int i = 0; i < count; i++)
                records.Add(new FsmTraceRecord
                {
                    Sequence = i + 1, MachineId = 1, RootId = 1, MachineName = "Player",
                    Time = i, Kind = FsmTraceKind.Enter, State = i == 0 ? null : i % 2 == 0 ? "Move" : "Idle",
                    Target = i % 2 == 0 ? "Idle" : "Move", Reason = StateChangeReason.LocalTransition,
                    RuleName = i % 2 == 0 ? "停止移动" : "开始移动"
                });
            return records;
        }

        private sealed class EmptyState : IState
        {
            public void Enter() { }
            public void Tick() { }
            public void FixedTick() { }
            public void Exit() { }
        }
    }
}
