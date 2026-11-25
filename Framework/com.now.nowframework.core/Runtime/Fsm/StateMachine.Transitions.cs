using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Core.Fsm
{
    public partial class StateMachine<TStateId>
    {
        /// <summary>注册指定来源的条件转移；优先级降序，同值按注册顺序检查。</summary>
        /// <param name="fromId">来源状态 ID。</param>
        /// <param name="targetId">目标状态 ID。</param>
        /// <param name="condition">无副作用的条件，每次 Tick 检查。</param>
        /// <param name="force">是否强制打断，忽略 CanExit 并覆盖未完成请求。</param>
        /// <param name="priority">数值越大越优先，但公共规则始终先于局部规则。</param>
        /// <param name="name">显示在历史和等待诊断中的名称。</param>
        public void AddTransition(TStateId fromId, TStateId targetId, Func<bool> condition, bool force = false,
            int priority = 0, string name = null)
        {
            EnsureConfigurable();
            GetState(fromId);
            ValidateTransition(targetId, condition);
            if (!_localTransitions.TryGetValue(fromId, out List<Transition> transitions))
            {
                transitions = new List<Transition>();
                _localTransitions.Add(fromId, transitions);
            }
            InsertTransition(transitions, new Transition(targetId, condition, force, priority, name));
        }

        /// <summary>注册公共条件转移；每次 Tick 先查公共规则，再查局部规则。</summary>
        /// <param name="targetId">已注册的目标状态 ID。</param>
        /// <param name="condition">只判断条件，不在其中启停或切换状态。</param>
        /// <param name="force">是否立即打断；死亡等最高优先级规则建议使用 true。</param>
        /// <param name="priority">数值越大越优先，同值保持注册顺序。</param>
        /// <param name="name">显示在历史和等待诊断中的名称。</param>
        public void AddGlobalTransition(TStateId targetId, Func<bool> condition, bool force = false,
            int priority = 0, string name = null)
        {
            EnsureConfigurable();
            ValidateTransition(targetId, condition);
            InsertTransition(_globalTransitions, new Transition(targetId, condition, force, priority, name));
        }

        /// <summary>注册局部事件转移，仅来源状态激活时可接收；优先级越大越先检查。</summary>
        public void AddEventTransition(TStateId fromId, string eventName, TStateId targetId,
            Func<bool> condition = null, bool force = false, int priority = 0, string name = null)
        {
            EnsureConfigurable();
            GetState(fromId);
            ValidateEventName(eventName);
            ValidateTransition(targetId, condition ?? AlwaysTrue);
            if (!_localEventTransitions.TryGetValue(fromId, out Dictionary<string, List<Transition>> events))
            {
                events = new Dictionary<string, List<Transition>>(StringComparer.Ordinal);
                _localEventTransitions.Add(fromId, events);
            }
            AddEventRule(events, eventName, new Transition(targetId, condition ?? AlwaysTrue, force, priority, name ?? eventName));
        }

        /// <summary>注册当前状态机范围内的公共事件转移，先于局部事件规则检查。</summary>
        public void AddGlobalEventTransition(string eventName, TStateId targetId, Func<bool> condition = null,
            bool force = false, int priority = 0, string name = null)
        {
            EnsureConfigurable();
            ValidateEventName(eventName);
            ValidateTransition(targetId, condition ?? AlwaysTrue);
            AddEventRule(_globalEventTransitions, eventName,
                new Transition(targetId, condition ?? AlwaysTrue, force, priority, name ?? eventName));
        }

        /// <summary>
        /// 向本机发送事件；回调中排队，回调外立即处理。以处理时的活动状态匹配规则。
        /// 等待退出时只接受匹配的强制规则，其他事件记为未处理，不延迟重放。
        /// </summary>
        public void SendEvent(string eventName)
        {
            EnsureNotEvaluating();
            ValidateEventName(eventName);
            if (!IsRunning) return;
            _pendingEvents.Enqueue(eventName);
            if (!_executing) Execute(_emptyCallback);
        }

        /// <summary>查找首条成立规则；等待中的普通请求只允许被强制规则替换。</summary>
        private bool CheckTransitions(List<Transition> transitions, StateChangeReason reason)
        {
            for (int i = 0; i < transitions.Count && IsRunning; i++) // i 为规则检查顺序。
            {
                Transition transition = transitions[i]; // 本次检查的规则。
                if (_waiting.HasValue && !transition.Force) continue;
                if (EqualityComparer<TStateId>.Default.Equals(CurrentStateId, transition.TargetId)) continue;
                bool matches; // 条件结果，判断期间禁止修改状态机。
                _evaluatingCondition = true;
                try { matches = transition.Condition(); }
                finally { _evaluatingCondition = false; }
                if (!matches) continue;
                RequestChange(transition.TargetId, transition.Force, false, false, reason, transition.Name);
                return true;
            }
            return false;
        }

        /// <summary>提交带原因的切换请求；强制请求覆盖尚未完成的旧请求。</summary>
        private void RequestChange(TStateId id, bool force, bool reenter, bool resetHistory, StateChangeReason reason,
            string ruleName = null)
        {
            GetState(id);
            if (!reenter && CurrentState != null && EqualityComparer<TStateId>.Default.Equals(CurrentStateId, id)) return;
            if (force) ClearTransitionRequests();
            _requests.Enqueue(new ChangeRequest(id, force, reenter, resetHistory, reason, ++_requestSequence, ruleName));
            if (!_executing) Execute(_emptyCallback);
        }

        /// <summary>按顺序推进请求，退出优先，等待退出的状态可继续正常更新。</summary>
        private void DrainRequests()
        {
            while (_exitRequested || (IsRunning && (_waiting.HasValue || _requests.Count > 0 || _pendingEvents.Count > 0)))
            {
                if (_exitRequested) { _exitRequested = false; ExitCurrentState(); return; }
                if (_pendingEvents.Count > 0 && (_waiting.HasValue || _requests.Count == 0))
                {
                    if (++_eventsThisPass > MaxTransitionsPerPass)
                        throw new InvalidOperationException("连续事件处理超过上限，请检查回调中的循环发事件。");
                    DispatchEvent(_pendingEvents.Dequeue());
                    if (!_waiting.HasValue && _requests.Count == 0) continue;
                }
                if (!_waiting.HasValue)
                {
                    _waiting = _requests.Dequeue();
                    _exitNotified = false;
                    _waitStarted = false;
                }
                ChangeRequest request = _waiting.Value; // 当前准备执行的请求。
                if (!request.Reenter && CurrentState != null &&
                    EqualityComparer<TStateId>.Default.Equals(CurrentStateId, request.TargetId))
                { ClearWaiting(); continue; }

                if (!request.Force && CurrentState is IStateExitHandler handler && !handler.CanExit)
                {
                    if (!_waitStarted)
                    {
                        _waitStarted = true;
                        _waitStartedAt = ReadDiagnosticTime();
                        WriteTrace(FsmTraceKind.WaitStarted, CurrentStateId, CurrentState != null,
                            request.TargetId, true, request.Reason, request.RuleName, "CanExit = false");
                    }
                    if (!_exitNotified)
                    {
                        _exitNotified = true;
                        handler.OnExitRequested();
                    }
                    if (!IsRunning || !_waiting.HasValue || _waiting.Value.Sequence != request.Sequence) continue;
                    if (!handler.CanExit)
                    {
                        if (_pendingEvents.Count > 0) continue;
                        return;
                    }
                }
                if (++_transitionsThisPass > MaxTransitionsPerPass)
                    throw new InvalidOperationException("连续切换超过上限，请检查 Enter/Exit 中的循环切换。");
                ClearWaiting(false);
                bool hasPrevious = CurrentState != null;
                TStateId previousId = CurrentStateId;
                ExitCurrentState(request.Reason, request.RuleName);
                if (!IsRunning) continue;
                if (request.ResetHistory) ResetHistoryTree();
                CurrentStateId = request.TargetId;
                CurrentState = GetState(request.TargetId);
                _stateEnteredAt = ReadDiagnosticTime();
                WriteTrace(FsmTraceKind.Enter, previousId, hasPrevious, request.TargetId, true,
                    request.Reason, request.RuleName);
                CurrentState.Enter();
                PublishChange(hasPrevious, previousId, request.TargetId, request.Reason, request.RuleName);
            }
        }

        /// <summary>稳定按优先级降序插入，同优先级保持先注册者在前。</summary>
        private static void InsertTransition(List<Transition> transitions, Transition transition)
        {
            int index = 0; // 第一个比新规则优先级低的位置。
            while (index < transitions.Count && transitions[index].Priority >= transition.Priority) index++;
            transitions.Insert(index, transition);
        }

        /// <summary>将事件规则加入对应事件名的列表。</summary>
        private static void AddEventRule(Dictionary<string, List<Transition>> events, string eventName, Transition transition)
        {
            if (!events.TryGetValue(eventName, out List<Transition> transitions))
            {
                transitions = new List<Transition>();
                events.Add(eventName, transitions);
            }
            InsertTransition(transitions, transition);
        }

        /// <summary>事件先匹配公共规则，再匹配处理时活动状态的局部规则。</summary>
        private void DispatchEvent(string eventName)
        {
            if (_globalEventTransitions.TryGetValue(eventName, out List<Transition> global) &&
                CheckTransitions(global, StateChangeReason.GlobalEvent)) return;
            if (CurrentState != null && _localEventTransitions.TryGetValue(CurrentStateId, out Dictionary<string, List<Transition>> events) &&
                events.TryGetValue(eventName, out List<Transition> local) && CheckTransitions(local, StateChangeReason.LocalEvent)) return;
            LastUnhandledEvent = eventName;
            WriteTrace(FsmTraceKind.UnhandledEvent, CurrentStateId, CurrentState != null, detail: eventName);
        }

        /// <summary>事件名不能为空或全空白，名称区分大小写。</summary>
        private static void ValidateEventName(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("事件名不能为空。", nameof(eventName));
        }

        /// <summary>校验转移目标及条件。</summary>
        private void ValidateTransition(TStateId targetId, Func<bool> condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            GetState(targetId);
        }

        private void ClearTransitionRequests()
        {
            _requests.Clear();
            ClearWaiting();
        }
    }
}
