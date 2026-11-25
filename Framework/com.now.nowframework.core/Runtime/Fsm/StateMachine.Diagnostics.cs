using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Core.Fsm
{
    public partial class StateMachine<TStateId>
    {
        /// <summary>增量复制轨迹快照，仅调试采集时调用。</summary>
        public long CopyTraceTo(List<FsmTraceRecord> destination, long afterVersion)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            return _trace.CopyTo(destination, afterVersion, this);
        }

        /// <summary>获取活动层级路径；用于界面观察，不要每帧构造。</summary>
        public string GetActivePath()
        {
            if (CurrentState == null) return "(stopped)";
            string current = Convert.ToString(CurrentStateId); // 当前层级的显示名称。
            return CurrentState is IStateMachineInfo child && child.IsRunning
                ? current + "/" + child.GetActivePath() : current;
        }

        /// <summary>构造有限长度的历史文本，仅供调试界面使用。</summary>
        public string GetHistoryText()
        {
            var text = new StringBuilder(); // 仅主动查询历史时分配。
            foreach (StateChange<TStateId> change in _history)
                text.Append(change.HasPrevious ? Convert.ToString(change.PreviousId) : "START")
                    .Append(" -> ").Append(change.CurrentId).Append(" [").Append(change.Reason)
                    .Append("] ").AppendLine(change.RuleName);
            return text.ToString();
        }

        /// <summary>清除本机及所有子状态机的恢复历史，保留调试记录。</summary>
        private void ResetHistoryTree()
        {
            _hasLastState = false;
            _lastStateId = default;
            foreach (IState state in _states.Values)
                if (state is IHistoryReset child) child.ResetHistory();
        }

        /// <summary>记录进入完成的状态并通知观察者，观察者异常不会停机。</summary>
        private void PublishChange(bool hasPrevious, TStateId previousId, TStateId currentId, StateChangeReason reason,
            string ruleName = null)
        {
            var change = new StateChange<TStateId>(hasPrevious, previousId, currentId, reason, ruleName); // 本次切换数据。
            if (_history.Count == HistoryCapacity) _history.RemoveAt(0);
            _history.Add(change);
            Exception observerError = _stateObservers.Invoke(change); // 快照只在订阅变更时创建。
            if (observerError != null) LastObserverException = observerError;
        }

        /// <summary>默认诊断时钟，使用单调计时，不依赖 Unity 帧更新。</summary>
        private static double ReadClock() { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; }

        private double ReadDiagnosticTime()
        {
            try
            {
                double time = _clock();
                if (!double.IsNaN(time) && !double.IsInfinity(time)) _lastDiagnosticTime = time;
            }
            catch (Exception exception) { LastDiagnosticsException = exception; }
            return _lastDiagnosticTime;
        }

        /// <summary>仅生命周期变化时记录，关闭轨迹时不分配缓冲或转换字符串。</summary>
        private void WriteTrace(FsmTraceKind kind, TStateId state = default, bool hasState = false,
            TStateId target = default, bool hasTarget = false, StateChangeReason reason = StateChangeReason.Manual,
            string ruleName = null, string detail = null, double duration = 0)
        {
            if (TraceEnabled)
                _trace.Add(ReadDiagnosticTime(), kind, state, hasState, target, hasTarget, reason, ruleName, detail, duration);
        }
    }
}
