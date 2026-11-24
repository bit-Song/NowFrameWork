#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NowFrameWork.Core.Fsm.Editor
{
    /// <summary>运行轨迹时间线与实时诊断；不编辑状态图或业务配置。</summary>
    public sealed partial class FsmDebuggerWindow : EditorWindow
    {
        private const float RowHeight = 52f; // 一条轨迹在时间线中占用的高度。
        private readonly List<FsmTraceRecord> _visible = new(); // 当前筛选后的轨迹。
        private readonly Dictionary<long, MachineRow> _machines = new(); // 从快照重建的层级。
        private readonly Dictionary<long, IStateMachineInfo> _liveMachines = new(); // 仅运行中持有，退出 Play 即释放。
        private List<FsmTraceRecord> _pausedRecords; // 暂停显示的独立快照，不停止后台采集。
        [SerializeField] private long _selectedMachine; // 会话内的状态机标识，0 为全部；停止 Play 后保留选择。
        private long _selectedSequence, _selectedRecordMachine; // 当前选中记录的发生序号与来源。
        private FsmTraceKind _selectedKind; // 区分覆盖提示与同序号的正常记录。
        private Vector2 _machineScroll, _traceScroll, _detailScroll;
        private bool _paused;
        [SerializeField] private bool _follow = true, _includeChildren = true;
        [SerializeField] private string _search = ""; // 名称、状态、规则或诊断文本筛选。
        [SerializeField] private int _kindFilter, _tab; // 记录类型筛选和右侧视图。
        private double _nextRepaint;
        private static readonly string[] KindFilters = { "全部记录", "状态变化", "退出等待", "事件转移", "异常 / 诊断" };

        /// <summary>暂停只选择冻结快照，不影响采集器或源状态机。</summary>
        internal IReadOnlyList<FsmTraceRecord> DisplayRecords => _paused && _pausedRecords != null
            ? _pausedRecords : FsmTraceSession.instance.Records;

        /// <summary>冻结或恢复当前显示，运行中的会话仍持续追加。</summary>
        internal void SetPaused(bool paused)
        {
            CapturePausedCardTimes(paused);
            _paused = paused;
            _pausedRecords = paused ? new List<FsmTraceRecord>(FsmTraceSession.instance.Records) : null;
        }

        private struct MachineRow
        {
            public long Id, ParentId, RootId;
            public int Depth;
            public string Name;
        }

        /// <summary>打开轨迹观察器，也可在停止 Play 后回看上一轮。</summary>
        [MenuItem("NowFrameWork/FSM/HFSM 观察器")]
        public static void Open()
        {
            FsmDebuggerWindow window = GetWindow<FsmDebuggerWindow>("HFSM 运行轨迹");
            window.minSize = new Vector2(850, 540);
        }

        private void OnEnable()
        {
            EditorApplication.update += Refresh;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Refresh;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _liveMachines.Clear();
        }

        /// <summary>新运行开始清除窗口选择，退出后只保留纯数据标识。</summary>
        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                _selectedMachine = 0; _selectedSequence = 0; _paused = false; _pausedRecords = null;
            }
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
                _liveMachines.Clear();
            Repaint();
        }

        /// <summary>只刷新显示，轨迹采集由独立记录器负责。</summary>
        private void Refresh()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + 0.2;
            Repaint();
        }

        private void OnGUI()
        {
            if (EditorApplication.isPlaying && !_paused) FsmTraceRecorder.Capture();
            FsmTraceSession session = FsmTraceSession.instance;
            DrawToolbar(session);
            IReadOnlyList<FsmTraceRecord> records = DisplayRecords;
            BuildMachineList(records);
            FilterRecords(records);
            EnsureCardTrail(records);
            EditorGUILayout.BeginHorizontal();
            DrawMachineList();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(EditorApplication.isPlaying ? "运行中 · 轨迹持续采集" : "已停止 · 回看最近一次运行", EditorStyles.boldLabel);
            EditorGUILayout.LabelField((session.Label ?? "尚无运行记录") + "    " + records.Count + " / 4096 条" +
                (session.Trimmed > 0 ? "    已淘汰最早 " + session.Trimmed + " 条" : ""), EditorStyles.miniLabel);
            _tab = GUILayout.Toolbar(_tab, new[] { "卡片轨迹", "事件时间线", "实时详情" });
            if (_tab == 0)
            {
                DrawCards(records);
                DrawSelectedRecord();
            }
            else if (_tab == 1)
            {
                DrawTimeline(records);
                DrawSelectedRecord();
            }
            else DrawLiveDetails();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>暂停仅冻结显示，筛选和清空都不会修改运行中的状态机。</summary>
        private void DrawToolbar(FsmTraceSession session)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(_paused ? "继续显示" : "暂停显示", EditorStyles.toolbarButton, GUILayout.Width(75)))
            {
                if (!_paused && EditorApplication.isPlaying) FsmTraceRecorder.Capture();
                SetPaused(!_paused);
            }
            _follow = GUILayout.Toggle(_follow, "跟随最新", EditorStyles.toolbarButton, GUILayout.Width(75));
            _includeChildren = GUILayout.Toggle(_includeChildren, "包含子层级", EditorStyles.toolbarButton, GUILayout.Width(85));
            _kindFilter = EditorGUILayout.Popup(_kindFilter, KindFilters, EditorStyles.toolbarPopup, GUILayout.Width(105));
            GUILayout.Label("搜索", GUILayout.Width(28));
            _search = GUILayout.TextField(_search, EditorStyles.toolbarTextField, GUILayout.MinWidth(80));
            if (GUILayout.Button("复制轨迹", EditorStyles.toolbarButton, GUILayout.Width(70))) CopyVisible();
            if (GUILayout.Button("清空显示", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                session.Clear(); _paused = false; _pausedRecords = null; _selectedSequence = 0;
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>依据纯文本快照重建层级，不依赖停止后已销毁的角色对象。</summary>
        private void BuildMachineList(IReadOnlyList<FsmTraceRecord> records)
        {
            _machines.Clear(); _liveMachines.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                FsmTraceRecord record = records[i];
                _machines[record.MachineId] = new MachineRow
                { Id = record.MachineId, ParentId = record.ParentId, RootId = record.RootId, Depth = record.Depth, Name = record.MachineName };
            }
            if (!EditorApplication.isPlaying) return;
            foreach (IStateMachineInfo machine in FsmDiagnostics.GetMachines())
            {
                long id = FsmTraceRecorder.GetSessionId(machine.DebugId);
                _liveMachines[id] = machine;
                if (_paused) continue;
                IStateMachineInfo root = machine;
                int depth = 0;
                while (root.Parent != null) { root = root.Parent; depth++; }
                _machines[id] = new MachineRow
                {
                    Id = id, ParentId = FsmTraceRecorder.GetSessionId(machine.Parent?.DebugId ?? 0),
                    RootId = FsmTraceRecorder.GetSessionId(root.DebugId), Depth = depth, Name = machine.Name
                };
            }
        }

        private void DrawMachineList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(215));
            EditorGUILayout.LabelField("状态机 / 层级", EditorStyles.boldLabel);
            _machineScroll = EditorGUILayout.BeginScrollView(_machineScroll);
            if (GUILayout.Toggle(_selectedMachine == 0, "全部状态机", "Button")) _selectedMachine = 0;
            foreach (MachineRow row in _machines.Values)
                if (row.ParentId == 0 || !_machines.ContainsKey(row.ParentId)) DrawMachineRow(row, 0);
            if (_machines.Count == 0)
                EditorGUILayout.HelpBox("进入 Play 后会自动采集，无需订阅事件。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawMachineRow(MachineRow row, int depth)
        {
            string label = new string(' ', depth * 3) + row.Name + "  #" + (uint)row.Id;
            string tooltip = "运行代次 " + (row.Id >> 32) + "，实例 " + (uint)row.Id;
            if (GUILayout.Toggle(_selectedMachine == row.Id, new GUIContent(label, tooltip), "Button")) _selectedMachine = row.Id;
            foreach (MachineRow child in _machines.Values)
                if (child.ParentId == row.Id) DrawMachineRow(child, depth + 1);
        }

        private bool IsInScope(FsmTraceRecord record)
        {
            if (_selectedMachine == 0 || record.MachineId == _selectedMachine) return true;
            if (!_includeChildren) return false;
            if (record.RootId == _selectedMachine) return true;
            long parentId = record.ParentId;
            for (int i = 0; parentId != 0 && i < _machines.Count; i++)
            {
                if (parentId == _selectedMachine) return true;
                if (!_machines.TryGetValue(parentId, out MachineRow parent)) break;
                parentId = parent.ParentId;
            }
            return false;
        }

        private void FilterRecords(IReadOnlyList<FsmTraceRecord> records)
        {
            _visible.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                FsmTraceRecord record = records[i];
                if (!IsInScope(record) || !MatchesKind(record)) continue;
                if (!string.IsNullOrEmpty(_search) && !Contains(record.MachineName, _search) &&
                    !Contains(record.State, _search) && !Contains(record.Target, _search) &&
                    !Contains(record.RuleName, _search) && !Contains(record.Detail, _search)) continue;
                _visible.Add(record);
            }
        }

        private bool MatchesKind(FsmTraceRecord record)
        {
            switch (_kindFilter)
            {
                case 1: return record.Kind == FsmTraceKind.Enter || record.Kind == FsmTraceKind.Exit || record.Kind == FsmTraceKind.Stopped;
                case 2: return record.Kind == FsmTraceKind.WaitStarted || record.Kind == FsmTraceKind.WaitEnded || record.Kind == FsmTraceKind.WaitCancelled;
                case 3: return record.Kind == FsmTraceKind.UnhandledEvent || record.Reason == StateChangeReason.LocalEvent || record.Reason == StateChangeReason.GlobalEvent;
                case 4: return record.Kind == FsmTraceKind.Fault || record.Kind == FsmTraceKind.Gap || record.Kind == FsmTraceKind.UnhandledEvent;
                default: return true;
            }
        }

        /// <summary>虚拟化绘制可见行，记录到达上限时也不创建数千个 GUI 控件。</summary>
        private void DrawTimeline(IReadOnlyList<FsmTraceRecord> records)
        {
            Rect viewport = GUILayoutUtility.GetRect(100, 100000, 180, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.ScrollWheel && viewport.Contains(Event.current.mousePosition)) _follow = false;
            float totalHeight = Math.Max(viewport.height, _visible.Count * RowHeight);
            if (_follow) _traceScroll.y = Math.Max(0, totalHeight - viewport.height);
            Rect content = new Rect(0, 0, Math.Max(1, viewport.width - 18), totalHeight);
            _traceScroll = GUI.BeginScrollView(viewport, _traceScroll, content);
            int first = Math.Max(0, (int)(_traceScroll.y / RowHeight));
            int last = Math.Min(_visible.Count, first + (int)(viewport.height / RowHeight) + 2);
            double origin = FsmTraceSession.instance.TimeOrigin;
            for (int i = first; i < last; i++) DrawTraceRow(_visible[i], new Rect(0, i * RowHeight, content.width, RowHeight), origin);
            if (_visible.Count == 0) GUI.Label(new Rect(12, 12, content.width - 24, 50), "没有匹配的轨迹。检查筛选条件，或运行状态机。", EditorStyles.wordWrappedLabel);
            GUI.EndScrollView();
        }

        private void DrawTraceRow(FsmTraceRecord record, Rect rect, double origin)
        {
            bool selected = record.Sequence == _selectedSequence && record.MachineId == _selectedRecordMachine && record.Kind == _selectedKind;
            if (selected) EditorGUI.DrawRect(rect, new Color(0.20f, 0.40f, 0.58f, 0.35f));
            else if (((int)(rect.y / RowHeight) & 1) == 0) EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.06f));
            EditorGUI.DrawRect(new Rect(14, rect.y, 2, rect.height), new Color(0.5f, 0.5f, 0.5f, 0.4f));
            EditorGUI.DrawRect(new Rect(11, rect.y + 16, 8, 8), TraceColor(record.Kind));
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) SelectRecord(record);
            GUI.Label(new Rect(28, rect.y + 3, 86, 20), "+" + Math.Max(0, record.Time - origin).ToString("F3") + "s", EditorStyles.miniLabel);
            GUI.Label(new Rect(116, rect.y + 3, rect.width - 122, 22), TraceTitle(record), selected ? EditorStyles.boldLabel : EditorStyles.label);
            string subtitle = record.MachineName + "  ·  " + KindLabel(record.Kind) + "  ·  " + ReasonLabel(record.Reason);
            if (!string.IsNullOrEmpty(record.RuleName)) subtitle += "  ·  " + record.RuleName;
            if (record.Duration > 0) subtitle += "  ·  持续 " + record.Duration.ToString("F3") + "s";
            GUI.Label(new Rect(28, rect.y + 26, rect.width - 34, 20), new GUIContent(subtitle, record.Detail), EditorStyles.miniLabel);
        }

        private void SelectRecord(FsmTraceRecord record, bool locate = false)
        {
            _selectedSequence = record.Sequence; _selectedRecordMachine = record.MachineId; _selectedKind = record.Kind;
            _follow = false;
            if (!locate) return;
            for (int i = 0; i < _visible.Count; i++)
                if (_visible[i].Sequence == record.Sequence && _visible[i].MachineId == record.MachineId && _visible[i].Kind == record.Kind)
                { _traceScroll.y = i * RowHeight; break; }
        }

        private void DrawSelectedRecord()
        {
            IReadOnlyList<FsmTraceRecord> records = DisplayRecords;
            for (int i = 0; i < records.Count; i++)
            {
                FsmTraceRecord record = records[i];
                if (!IsInScope(record)) continue;
                if (record.Sequence != _selectedSequence || record.MachineId != _selectedRecordMachine || record.Kind != _selectedKind) continue;
                FsmStateCard card = record.Kind == FsmTraceKind.Enter ? _cardTrail.Find(record.MachineId, record.Sequence) : null;
                string duration = card != null ? CardDurationLabel(card) : record.Duration.ToString("F3") + "s";
                string text = record.MachineName + "  #" + (uint)record.MachineId + "  ·  " + KindLabel(record.Kind) + "\n" +
                    TraceTitle(record) + "\n原因：" + ReasonLabel(record.Reason) + "    规则：" + (record.RuleName ?? "—") +
                    "\n持续：" + duration + "    " + (record.Detail ?? "");
                EditorGUILayout.SelectableLabel(text, EditorStyles.textArea, GUILayout.Height(82));
                return;
            }
            EditorGUILayout.HelpBox("点击记录查看详情。暂停显示不会暂停游戏；停止 Play 后仍可回看，下一次 Play 会开始新会话。", MessageType.Info);
        }

        /// <summary>保留原有实时诊断和根状态机操作，归档模式下不持有运行对象。</summary>
        private void DrawLiveDetails()
        {
            if (!EditorApplication.isPlaying || !_liveMachines.TryGetValue(_selectedMachine, out IStateMachineInfo machine))
            { EditorGUILayout.HelpBox("运行中选择一台状态机可查看实时详情。停止后请切回运行轨迹。", MessageType.Info); return; }
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            EditorGUILayout.LabelField(machine.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("活动路径", machine.GetActivePath());
            EditorGUILayout.LabelField("运行状态", machine.IsFaulted ? "故障停止" : machine.IsRunning ? "运行中" : "已停止");
            EditorGUILayout.LabelField("轨迹采集", machine.TraceEnabled ? "开启" : "关闭");
            if (machine.PendingTarget != null)
            {
                EditorGUILayout.LabelField("等待目标", machine.PendingTarget);
                EditorGUILayout.LabelField("转移规则", machine.PendingRuleName);
                EditorGUILayout.LabelField("等待时间", machine.ExitWaitSeconds.ToString("F2") + " 秒");
                try { EditorGUILayout.LabelField("等待原因", machine.ExitWaitReason); }
                catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }
                if (machine.IsExitWaitOverdue) EditorGUILayout.HelpBox("退出等待超过阈值，请检查 CanExit 条件。", MessageType.Warning);
            }
            if (machine.LastUnhandledEvent != null) EditorGUILayout.LabelField("未处理事件", machine.LastUnhandledEvent);
            if (machine.Parent == null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("重启")) RunCommand(() => machine.Restart());
                if (GUILayout.Button("停止")) RunCommand(() => machine.Exit());
                EditorGUILayout.EndHorizontal();
            }
            DrawException("业务异常", machine.LastException);
            DrawException("清理异常", machine.LastCleanupException);
            DrawException("观察者异常", machine.LastObserverException);
            DrawException("诊断时钟异常", machine.LastDiagnosticsException);
            if (GUILayout.Button("校验整棵配置"))
            {
                List<string> errors = machine.ValidateConfiguration();
                if (errors.Count == 0) Debug.Log("[HFSM] 配置校验通过：" + machine.Name);
                else Debug.LogError("[HFSM] 配置错误：\n" + string.Join("\n", errors));
            }
            EditorGUILayout.EndScrollView();
        }

        private void CopyVisible()
        {
            var text = new StringBuilder();
            foreach (FsmTraceRecord record in _visible)
                text.Append(record.Time.ToString("F6")).Append("\t").Append(record.MachineName).Append("\t")
                    .Append(KindLabel(record.Kind)).Append("\t").Append(TraceTitle(record)).Append("\t")
                    .Append(ReasonLabel(record.Reason)).Append("\t").Append(record.RuleName).Append("\t")
                    .Append(record.Duration.ToString("F6")).Append("s\t").AppendLine(record.Detail);
            EditorGUIUtility.systemCopyBuffer = text.ToString();
        }

        private static bool Contains(string value, string search) { return value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0; }
        private static void DrawException(string title, Exception exception) { if (exception != null) EditorGUILayout.HelpBox(title + "\n" + exception, MessageType.Error); }
        private static void RunCommand(Action command) { try { command(); } catch (Exception exception) { Debug.LogException(exception); } }
        private static string TraceTitle(FsmTraceRecord record)
        {
            switch (record.Kind)
            {
                case FsmTraceKind.Enter: return record.State == null ? "进入 " + record.Target : record.State + " → " + record.Target;
                case FsmTraceKind.Exit: return "退出 " + record.State;
                case FsmTraceKind.WaitStarted: return record.State + " 等待 → " + record.Target;
                case FsmTraceKind.WaitEnded: return "允许退出 → " + record.Target;
                case FsmTraceKind.WaitCancelled: return "等待已取消 → " + record.Target;
                case FsmTraceKind.Stopped: return "停止状态机";
                case FsmTraceKind.UnhandledEvent: return "未处理事件：" + record.Detail;
                case FsmTraceKind.Fault: return "异常：" + record.Detail;
                default: return record.Detail ?? "轨迹缺失";
            }
        }
        private static string KindLabel(FsmTraceKind kind)
        {
            switch (kind)
            {
                case FsmTraceKind.Enter: return "进入"; case FsmTraceKind.Exit: return "退出";
                case FsmTraceKind.WaitStarted: return "开始等待"; case FsmTraceKind.WaitEnded: return "等待结束";
                case FsmTraceKind.WaitCancelled: return "取消等待"; case FsmTraceKind.Stopped: return "停止";
                case FsmTraceKind.UnhandledEvent: return "事件未处理"; case FsmTraceKind.Fault: return "异常";
                default: return "记录缺失";
            }
        }
        private static string ReasonLabel(StateChangeReason reason)
        {
            switch (reason)
            {
                case StateChangeReason.Initial: return "初始进入"; case StateChangeReason.History: return "恢复历史";
                case StateChangeReason.LocalTransition: return "局部条件"; case StateChangeReason.GlobalTransition: return "公共条件";
                case StateChangeReason.LocalEvent: return "局部事件"; case StateChangeReason.GlobalEvent: return "公共事件";
                case StateChangeReason.Restart: return "重启"; case StateChangeReason.Reenter: return "重新进入";
                default: return "手动请求";
            }
        }
        private static Color TraceColor(FsmTraceKind kind)
        {
            switch (kind)
            {
                case FsmTraceKind.Enter: return new Color(0.25f, 0.8f, 0.6f);
                case FsmTraceKind.WaitStarted: case FsmTraceKind.WaitCancelled: return new Color(1f, 0.7f, 0.2f);
                case FsmTraceKind.Fault: return new Color(1f, 0.3f, 0.3f);
                case FsmTraceKind.Gap: return new Color(0.9f, 0.4f, 1f);
                case FsmTraceKind.UnhandledEvent: return new Color(1f, 0.5f, 0.2f);
                default: return new Color(0.55f, 0.65f, 0.8f);
            }
        }
    }
}
#endif
