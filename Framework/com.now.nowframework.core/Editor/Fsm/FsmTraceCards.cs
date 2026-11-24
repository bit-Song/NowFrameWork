#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace NowFrameWork.Core.Fsm.Editor
{
    internal enum FsmCardLink { None, Transition, EnterAgain, Missing }

    /// <summary>一次实际进入的状态卡片，同名状态再次进入时创建新卡片。</summary>
    internal sealed class FsmStateCard
    {
        public FsmTraceRecord Enter; // 对应的进入记录，也是点击卡片时定位的目标。
        public FsmTraceRecord LastWait, Fault; // 等待和异常的原始详情。
        public bool HasExit, HasSuccessor, IsStopped, HasFault, IsWaiting, BoundaryLost;
        public double Duration; // 仅由匹配的 Exit 记录确定，不猜测缺失时间。
        public FsmCardLink Link; // 与本行前一张卡片的实际关系。
        public bool IsOpen => !HasExit && !HasSuccessor && !IsStopped && !HasFault && !BoundaryLost;
    }

    /// <summary>一台状态机一条轨道，父子状态机不互相连线。</summary>
    internal sealed class FsmCardLane
    {
        public long MachineId, ParentId, RootId;
        public string Name;
        public int Depth;
        public bool HasGap, GapPending;
        public readonly List<FsmStateCard> Cards = new();
    }

    /// <summary>从完整轨迹还原状态访问，不从文本筛选结果推断缺失转移。</summary>
    internal sealed class FsmCardTrail
    {
        private readonly List<FsmCardLane> _lanes = new();
        private readonly Dictionary<long, FsmCardLane> _byId = new();
        public IReadOnlyList<FsmCardLane> Lanes => _lanes;

        /// <summary>每次数据版本改变时重建；每条轨道独立匹配 Enter 和 Exit。</summary>
        public void Build(IReadOnlyList<FsmTraceRecord> records)
        {
            _lanes.Clear(); _byId.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                FsmTraceRecord record = records[i];
                if (!_byId.TryGetValue(record.MachineId, out FsmCardLane lane))
                {
                    lane = new FsmCardLane { MachineId = record.MachineId };
                    _byId.Add(record.MachineId, lane); _lanes.Add(lane);
                }
                lane.Name = record.MachineName; lane.ParentId = record.ParentId;
                lane.RootId = record.RootId; lane.Depth = record.Depth;
                FsmStateCard previous = lane.Cards.Count > 0 ? lane.Cards[lane.Cards.Count - 1] : null;
                if (record.Kind == FsmTraceKind.Gap)
                {
                    lane.HasGap = lane.GapPending = true;
                    if (previous != null) { previous.BoundaryLost = true; previous.IsWaiting = false; }
                    continue;
                }
                if (record.Kind == FsmTraceKind.Enter)
                {
                    var card = new FsmStateCard { Enter = record };
                    if (previous != null)
                    {
                        if (lane.GapPending || !previous.HasExit) card.Link = FsmCardLink.Missing;
                        else if (record.State == null) card.Link = FsmCardLink.EnterAgain;
                        else card.Link = string.Equals(previous.Enter.Target, record.State, StringComparison.Ordinal)
                            ? FsmCardLink.Transition : FsmCardLink.Missing;
                        previous.HasSuccessor = true;
                    }
                    else if (lane.GapPending) card.Link = FsmCardLink.Missing;
                    lane.Cards.Add(card); lane.GapPending = false;
                    continue;
                }
                if (previous == null || lane.GapPending) continue;
                bool sameState = string.Equals(record.State, previous.Enter.Target, StringComparison.Ordinal);
                if (record.Kind == FsmTraceKind.Exit && !sameState)
                {
                    previous.BoundaryLost = true; previous.IsWaiting = false;
                    lane.HasGap = lane.GapPending = true;
                    continue;
                }
                if (record.Kind == FsmTraceKind.Exit && sameState && !previous.HasExit)
                {
                    previous.HasExit = true; previous.Duration = record.Duration; previous.IsWaiting = false;
                }
                else if (record.Kind == FsmTraceKind.WaitStarted && sameState)
                {
                    previous.IsWaiting = true; previous.LastWait = record;
                }
                else if ((record.Kind == FsmTraceKind.WaitEnded || record.Kind == FsmTraceKind.WaitCancelled) && sameState)
                {
                    previous.IsWaiting = false; previous.LastWait = record;
                }
                else if (record.Kind == FsmTraceKind.Stopped && (sameState || record.State == null)) previous.IsStopped = true;
                else if (record.Kind == FsmTraceKind.Fault && (sameState || record.State == null))
                {
                    previous.HasFault = true; previous.Fault = record; previous.IsWaiting = false;
                }
            }
        }

        /// <summary>按进入记录定位访问实例，不按状态名合并。</summary>
        public FsmStateCard Find(long machineId, long sequence)
        {
            if (!_byId.TryGetValue(machineId, out FsmCardLane lane)) return null;
            for (int i = lane.Cards.Count - 1; i >= 0; i--)
                if (lane.Cards[i].Enter.Sequence == sequence) return lane.Cards[i];
            return null;
        }
    }
}
#endif
