#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NowFrameWork.Core.Fsm.Editor
{
    public sealed partial class FsmDebuggerWindow
    {
        private const float CardWidth = 150, CardHeight = 94, CardGap = 110, LaneHeight = 145, LaneLabelWidth = 145;
        private readonly FsmCardTrail _cardTrail = new(); // 从原始轨迹还原的状态访问。
        private readonly List<FsmCardLane> _cardLanes = new(); // 当前机器范围内的轨道。
        private readonly Dictionary<long, double> _pausedCardTimes = new(); // 暂停显示时冻结的当前停留时间。
        private IReadOnlyList<FsmTraceRecord> _cardSource; // 上一次构建模型使用的数据源。
        private int _cardVersion = int.MinValue; // 仅新轨迹到达时重建模型。
        [SerializeField] private int _cardLimitChoice = 1; // 默认每行显示最近 50 次访问。
        private Vector2 _cardScroll; // 卡片画布的横向和纵向滚动位置。
        private GUIStyle _cardTitleStyle, _cardTextStyle, _laneTitleStyle;
        private static readonly string[] CardLimits = { "最近 20 次", "最近 50 次", "最近 100 次", "全部访问" };
        private static readonly int[] CardLimitValues = { 20, 50, 100, int.MaxValue };
        internal Rect LastCardBounds { get; private set; } // 可见卡片的窗口坐标，供交互验证。
        internal Rect LastArrowBounds { get; private set; } // 可见箭头按钮的窗口坐标。
        internal FsmTraceRecord LastCardRecord { get; private set; }
        internal FsmTraceRecord LastArrowRecord { get; private set; }

        /// <summary>暂停只冻结显示数据和实时时长，不修改源状态机。</summary>
        private void CapturePausedCardTimes(bool paused)
        {
            _pausedCardTimes.Clear();
            if (!paused) return;
            foreach (KeyValuePair<long, IStateMachineInfo> pair in _liveMachines)
                if (pair.Value.IsRunning && pair.Value.CurrentState != null)
                    _pausedCardTimes[pair.Key] = pair.Value.CurrentStateSeconds;
        }

        /// <summary>完整原始记录用于配对退出，避免筛选后错误计算停留时间或连线。</summary>
        private void EnsureCardTrail(IReadOnlyList<FsmTraceRecord> records)
        {
            int version = _paused ? -1 : FsmTraceSession.instance.Version;
            if (ReferenceEquals(_cardSource, records) && _cardVersion == version) return;
            _cardTrail.Build(records); _cardSource = records; _cardVersion = version;
        }

        /// <summary>每台状态机单独一行；只连接实际捕获的相邻访问。</summary>
        private void DrawCards(IReadOnlyList<FsmTraceRecord> records)
        {
            EnsureCardStyles();
            LastCardBounds = LastArrowBounds = Rect.zero;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("实际执行路径 · 点击卡片或箭头查看详情", EditorStyles.miniBoldLabel);
            _cardLimitChoice = EditorGUILayout.Popup(_cardLimitChoice, CardLimits, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("绿色：当前状态   黄色：等待退出   红色：异常   虚线：再次进入或边界缺失", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("搜索命中卡片会高亮；保留中间状态，避免筛选后画出不存在的转移。", EditorStyles.miniLabel);

            _cardLanes.Clear();
            int maxCards = 0;
            int limit = CardLimitValues[Mathf.Clamp(_cardLimitChoice, 0, CardLimitValues.Length - 1)];
            foreach (FsmCardLane lane in _cardTrail.Lanes)
            {
                var scope = new FsmTraceRecord { MachineId = lane.MachineId, ParentId = lane.ParentId, RootId = lane.RootId };
                if (!IsInScope(scope)) continue;
                _cardLanes.Add(lane);
                maxCards = Math.Max(maxCards, Math.Min(limit, lane.Cards.Count));
            }
            Rect area = GUILayoutUtility.GetRect(100, 100000, 180, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, EditorGUIUtility.isProSkin ? new Color(.11f, .12f, .14f) : new Color(.95f, .96f, .97f));
            if (_cardLanes.Count == 0)
            {
                GUI.Label(new Rect(area.x + 16, area.y + 20, area.width - 32, 55), "运行状态机后，实际经过的状态会显示为卡片。也可以选择左侧已有记录的状态机。", EditorStyles.wordWrappedLabel);
                return;
            }
            Rect viewport = new Rect(area.x + LaneLabelWidth, area.y, Math.Max(1, area.width - LaneLabelWidth), area.height);
            if (Event.current.type == EventType.ScrollWheel && area.Contains(Event.current.mousePosition)) _follow = false;
            float contentWidth = Math.Max(viewport.width - 16, maxCards * (CardWidth + CardGap) - CardGap + 24);
            float contentHeight = Math.Max(viewport.height - 16, _cardLanes.Count * LaneHeight);
            if (_follow) _cardScroll.x = Math.Max(0, contentWidth - viewport.width + 16);
            _cardScroll = GUI.BeginScrollView(viewport, _cardScroll, new Rect(0, 0, contentWidth, contentHeight));
            int firstLane = Math.Max(0, (int)(_cardScroll.y / LaneHeight));
            int lastLane = Math.Min(_cardLanes.Count, firstLane + (int)(viewport.height / LaneHeight) + 2);
            for (int row = firstLane; row < lastLane; row++)
            {
                FsmCardLane lane = _cardLanes[row];
                int count = Math.Min(limit, lane.Cards.Count);
                int firstCard = lane.Cards.Count - count;
                float offset = 12 + (maxCards - count) * (CardWidth + CardGap); // 各行最新状态在右侧对齐。
                EditorGUI.DrawRect(new Rect(0, row * LaneHeight, contentWidth, 1), new Color(.5f, .5f, .5f, .15f));
                if (count == 0) GUI.Label(new Rect(12, row * LaneHeight + 45, 350, 25), "此轨道尚无完整进入记录", EditorStyles.miniLabel);
                for (int column = 0; column < count; column++)
                {
                    FsmStateCard card = lane.Cards[firstCard + column];
                    Rect cardRect = new Rect(offset + column * (CardWidth + CardGap), row * LaneHeight + 24, CardWidth, CardHeight);
                    if (column > 0 && cardRect.x - CardGap < _cardScroll.x + viewport.width && cardRect.x > _cardScroll.x)
                    {
                        DrawCardArrow(card, new Rect(cardRect.x - CardGap, cardRect.y, CardGap, CardHeight));
                        Rect arrowButton = new Rect(viewport.x + cardRect.x - CardGap + 5 - _cardScroll.x,
                            viewport.y + cardRect.y + 20 - _cardScroll.y, CardGap - 10, 22);
                        if (viewport.Contains(arrowButton.center)) { LastArrowBounds = arrowButton; LastArrowRecord = card.Enter; }
                    }
                    if (cardRect.xMax >= _cardScroll.x && cardRect.x <= _cardScroll.x + viewport.width)
                    {
                        DrawStateCard(card, cardRect);
                        Rect windowRect = new Rect(viewport.x + cardRect.x - _cardScroll.x, viewport.y + cardRect.y - _cardScroll.y, CardWidth, CardHeight);
                        if (viewport.Contains(windowRect.center)) { LastCardBounds = windowRect; LastCardRecord = card.Enter; }
                    }
                }
            }
            GUI.EndScrollView();

            // 左侧轨道名固定，不跟随横向滚动，父子层级始终可辨认。
            GUI.BeginGroup(new Rect(area.x, area.y, LaneLabelWidth, area.height));
            for (int row = firstLane; row < lastLane; row++)
            {
                FsmCardLane lane = _cardLanes[row];
                float y = row * LaneHeight - _cardScroll.y;
                EditorGUI.DrawRect(new Rect(0, y, LaneLabelWidth, 1), new Color(.5f, .5f, .5f, .15f));
                string label = (lane.Depth > 0 ? new string(' ', Math.Min(lane.Depth, 4) * 2) + "└ " : "") + lane.Name;
                GUI.Label(new Rect(8, y + 26, LaneLabelWidth - 12, 24), new GUIContent(label, lane.Name), _laneTitleStyle);
                GUI.Label(new Rect(8, y + 54, LaneLabelWidth - 12, 20), "#" + (uint)lane.MachineId + " · " + lane.Cards.Count + " 次进入", EditorStyles.miniLabel);
                GUI.Label(new Rect(8, y + 77, LaneLabelWidth - 12, 20), lane.HasGap ? "有缺失记录" : "显示最近 " + Math.Min(limit, lane.Cards.Count) + " 次", EditorStyles.miniLabel);
            }
            GUI.EndGroup();
        }

        /// <summary>状态是可点击按钮，状态相同但进入次数不同也分别显示。</summary>
        private void DrawStateCard(FsmStateCard card, Rect rect)
        {
            bool current = IsCurrentCard(card);
            Color accent = card.HasFault ? new Color(.95f, .32f, .32f) : card.IsWaiting ? new Color(1f, .70f, .20f) :
                current ? new Color(.23f, .82f, .49f) : new Color(.40f, .57f, .77f);
            Color background = Color.Lerp(EditorGUIUtility.isProSkin ? new Color(.17f, .18f, .21f) : Color.white, accent, current || card.IsWaiting || card.HasFault ? .24f : .07f);
            bool selected = card.Enter.Sequence == _selectedSequence && card.Enter.MachineId == _selectedRecordMachine && _selectedKind == FsmTraceKind.Enter;
            bool matches = !string.IsNullOrEmpty(_search) && (Contains(card.Enter.Target, _search) || Contains(card.Enter.RuleName, _search) ||
                Contains(card.Enter.MachineName, _search) || Contains(card.Fault.Detail, _search) || Contains(card.LastWait.RuleName, _search));
            EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), selected ? new Color(.25f, .7f, 1f) : matches ? new Color(1f, .85f, .25f) : new Color(.1f, .1f, .1f, .5f));
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 4), accent);
            string status = card.HasFault ? "异常" : card.IsWaiting ? "等待退出 → " + card.LastWait.Target : current ? (_paused ? "暂停快照" : "当前状态") :
                card.IsStopped ? "已停止" : card.HasExit ? "已结束" : "边界未完整记录";
            string tooltip = card.Enter.Target + "\n" + CardDurationLabel(card) + "\n" + status + "\n规则：" + (card.Enter.RuleName ?? ReasonLabel(card.Enter.Reason));
            Rect mainButton = card.HasFault || card.IsWaiting ? new Rect(rect.x, rect.y, rect.width, 64) : rect;
            if (GUI.Button(mainButton, new GUIContent("", tooltip), GUIStyle.none)) SelectCardRecord(card.Enter);
            GUI.Label(new Rect(rect.x + 6, rect.y + 12, rect.width - 12, 24), card.Enter.Target, _cardTitleStyle);
            GUI.Label(new Rect(rect.x + 4, rect.y + 41, rect.width - 8, 20), CardDurationLabel(card), _cardTextStyle);
            Rect statusRect = new Rect(rect.x + 4, rect.y + 66, rect.width - 8, 20);
            if (card.HasFault)
            {
                if (GUI.Button(statusRect, new GUIContent("异常 · 查看详情", card.Fault.Detail), EditorStyles.miniButton)) SelectCardRecord(card.Fault);
            }
            else if (card.IsWaiting)
            {
                if (GUI.Button(statusRect, new GUIContent(status, card.LastWait.RuleName), EditorStyles.miniButton)) SelectCardRecord(card.LastWait);
            }
            else GUI.Label(statusRect, status, _cardTextStyle);
        }

        /// <summary>箭头对应目标 Enter 的真实原因；缺失边界只画虚线，不补造转移。</summary>
        private void DrawCardArrow(FsmStateCard target, Rect area)
        {
            bool missing = target.Link == FsmCardLink.Missing;
            bool enterAgain = target.Link == FsmCardLink.EnterAgain;
            string label = missing ? "记录缺失" : enterAgain ? "再次进入" : target.Enter.RuleName ?? ReasonLabel(target.Enter.Reason);
            Color color = missing ? new Color(.65f, .65f, .65f) : enterAgain ? new Color(.7f, .55f, .9f) : new Color(.4f, .7f, .9f);
            Vector3 start = new Vector3(area.x + 7, area.y + 59);
            Vector3 end = new Vector3(area.xMax - 7, start.y);
            Handles.BeginGUI();
            Color previous = Handles.color;
            Handles.color = color;
            if (missing || enterAgain) Handles.DrawDottedLine(start, end, 4);
            else Handles.DrawAAPolyLine(2.5f, start, end);
            if (!missing) Handles.DrawAAPolyLine(2.5f, end + new Vector3(-8, -5), end, end + new Vector3(-8, 5));
            Handles.color = previous;
            Handles.EndGUI();
            string tooltip = missing ? "记录边界不完整，未推断中间转移。点击查看实际进入记录。" :
                ReasonLabel(target.Enter.Reason) + "\n" + (target.Enter.RuleName ?? "未命名规则");
            if (GUI.Button(new Rect(area.x + 5, area.y + 20, area.width - 10, 22), new GUIContent(label, tooltip), EditorStyles.miniButton))
                SelectCardRecord(target.Enter);
        }

        /// <summary>进入记录独立于明细筛选选中，搜索不会把中间卡片删掉。</summary>
        private void SelectCardRecord(FsmTraceRecord record) { SelectRecord(record, true); }

        /// <summary>只将有完整开边界且源状态机仍运行的最新访问标为当前。</summary>
        internal bool IsCurrentCard(FsmStateCard card)
        {
            if (!card.IsOpen) return false;
            if (_paused) return _pausedCardTimes.ContainsKey(card.Enter.MachineId);
            return _liveMachines.TryGetValue(card.Enter.MachineId, out IStateMachineInfo machine) && machine.TraceEnabled && machine.IsRunning && machine.CurrentState != null;
        }

        /// <summary>历史访问使用 Exit 时长；当前访问使用实时时钟，暂停时使用冻结值。</summary>
        internal string CardDurationLabel(FsmStateCard card)
        {
            if (card.HasExit) return "停留 " + card.Duration.ToString("F3") + " s";
            if (IsCurrentCard(card))
            {
                double seconds = _paused ? _pausedCardTimes[card.Enter.MachineId] : _liveMachines[card.Enter.MachineId].CurrentStateSeconds;
                return "停留 " + seconds.ToString("F3") + " s";
            }
            return "时长未完整记录";
        }

        /// <summary>只在 GUI 环境中创建样式，不在静态初始化时访问 EditorStyles。</summary>
        private void EnsureCardStyles()
        {
            if (_cardTitleStyle != null) return;
            _cardTitleStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 15, clipping = TextClipping.Clip };
            _cardTextStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            _laneTitleStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
        }
    }
}
#endif
