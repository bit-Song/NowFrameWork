using System;
using System.Collections.Generic;
using System.Threading;

namespace NowFrameWork.Core.Fsm
{
    /// <summary>运行轨迹中的生命周期与诊断节点，不记录每帧 Tick。</summary>
    public enum FsmTraceKind { Enter, Exit, WaitStarted, WaitEnded, WaitCancelled, Stopped, UnhandledEvent, Fault, Gap }

    /// <summary>编辑器快照：仅包含数值和文本，不持有角色、状态或异常对象。</summary>
    [Serializable]
    public struct FsmTraceRecord
    {
        public long Sequence; // 跨状态机的发生顺序，用于合并父子轨迹。
        public long MachineId, ParentId, RootId; // 当前编辑器运行代次内的实例标识。
        public int Depth; // 父子层级深度。
        public string MachineName; // 状态机名称快照。
        /// <summary>记录时间与状态停留时长。</summary>
        public double Time, Duration; // 单调时钟时间与本次状态/等待持续时间。
        /// <summary>记录类型。</summary>
        public FsmTraceKind Kind; // 记录类型。
        public string State, Target; // 来源/退出状态与进入/等待目标。
        /// <summary>切换原因。</summary>
        public StateChangeReason Reason; // 切换来源。
        /// <summary>规则名与诊断说明。</summary>
        public string RuleName, Detail; // 规则名与诊断说明。
    }

    /// <summary>非泛型计数器，保证不同状态 ID 类型共享同一发生顺序。</summary>
    internal static class FsmTraceSequence
    {
        private static long _sequence; // 所有状态机共用的轨迹序号。
        private static long _machineId; // 所有状态机共用的实例标识。
        /// <summary>取下一个全局轨迹序号。</summary>
        public static long Next() { return Interlocked.Increment(ref _sequence); }
        /// <summary>取下一个状态机实例标识。</summary>
        public static long NextMachineId() { return Interlocked.Increment(ref _machineId); }
    }

    /// <summary>保留泛型 ID 的有界环形缓冲；写入不转换字符串，不逐条分配。</summary>
    internal sealed class FsmTraceBuffer<TStateId>
    {
        private const int Capacity = 256; // 每台状态机最多保留的原始记录。
        private Entry[] _entries; // 启用轨迹后首次记录时才分配。
        private int _start, _count; // 最早记录位置与当前记录数量。
        private long _version; // 本机累计写入序号，用于增量采集。

        private struct Entry
        {
            /// <summary>轨迹版本与全局序号。</summary>
            public long Version, Sequence;
            public double Time, Duration;
            public FsmTraceKind Kind;
            /// <summary>来源状态与目标状态。</summary>
            public TStateId State, Target;
            /// <summary>状态/目标是否有效（默认 ID 不代表存在）。</summary>
            public bool HasState, HasTarget;
            public StateChangeReason Reason;
            public string RuleName, Detail;
        }

        /// <summary>当前写入版本，编辑器可据此跳过没有新记录的状态机。</summary>
        public long Version => _version;

        /// <summary>追加一条记录，容量满后覆盖最早记录。</summary>
        public void Add(double time, FsmTraceKind kind, TStateId state, bool hasState,
            TStateId target, bool hasTarget, StateChangeReason reason, string ruleName, string detail, double duration)
        {
            if (_entries == null) _entries = new Entry[Capacity];
            int index = (_start + _count) % Capacity;
            _entries[index] = new Entry
            {
                Version = ++_version, Sequence = FsmTraceSequence.Next(), Time = time,
                Kind = kind, State = state, HasState = hasState, Target = target, HasTarget = hasTarget,
                Reason = reason, RuleName = ruleName, Detail = detail, Duration = Math.Max(0, duration)
            };
            if (_count < Capacity) _count++;
            else _start = (_start + 1) % Capacity;
        }

        /// <summary>只在编辑器采集时转换文本，并标记采集间隔内被覆盖的记录。</summary>
        public long CopyTo(List<FsmTraceRecord> destination, long afterVersion, IStateMachineInfo machine)
        {
            if (_count == 0 || afterVersion >= _version) return _version;
            IStateMachineInfo root = machine;
            int depth = 0;
            while (root.Parent != null) { root = root.Parent; depth++; }
            Entry first = _entries[_start];
            if (first.Version > afterVersion + 1)
                destination.Add(new FsmTraceRecord
                {
                    Sequence = first.Sequence, Time = first.Time, MachineId = machine.DebugId,
                    ParentId = machine.Parent?.DebugId ?? 0, RootId = root.DebugId, Depth = depth,
                    MachineName = machine.Name, Kind = FsmTraceKind.Gap,
                    Detail = "采集前已覆盖 " + (first.Version - afterVersion - 1) + " 条记录"
                });
            for (int i = 0; i < _count; i++)
            {
                Entry entry = _entries[(_start + i) % Capacity];
                if (entry.Version <= afterVersion) continue;
                destination.Add(new FsmTraceRecord
                {
                    Sequence = entry.Sequence, MachineId = machine.DebugId,
                    ParentId = machine.Parent?.DebugId ?? 0, RootId = root.DebugId, Depth = depth,
                    MachineName = machine.Name, Time = entry.Time, Duration = entry.Duration, Kind = entry.Kind,
                    State = entry.HasState ? Convert.ToString(entry.State) : null,
                    Target = entry.HasTarget ? Convert.ToString(entry.Target) : null,
                    Reason = entry.Reason, RuleName = entry.RuleName, Detail = entry.Detail
                });
            }
            return _version;
        }
    }
}
