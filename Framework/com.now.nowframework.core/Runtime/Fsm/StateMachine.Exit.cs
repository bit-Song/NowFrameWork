using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Core.Fsm
{
    public partial class StateMachine<TStateId>
    {
        /// <summary>立即禁止更新，取消切换，在回调结束后强制退出并清理。</summary>
        public void Exit()
        {
            EnsureRoot();
            ExitOwned();
        }

        private void ExitOwned()
        {
            EnsureNotEvaluating();
            if (!IsRunning && (!HasPendingCleanup || _executing)) return;
            if (IsRunning) WriteTrace(FsmTraceKind.Stopped, CurrentStateId, CurrentState != null);
            IsRunning = false;
            _exitRequested = true;
            ClearRequests();
            if (!_executing) Execute(_emptyCallback);
        }

        /// <summary>父级等待退出时，将准备退出通知传递到当前子状态。</summary>
        public void OnExitRequested()
        {
            if (!IsRunning) return;
            if (_executing) NotifyCurrentExitRequest();
            else Execute(_exitRequestCallback);
        }

        /// <summary>先摘除活动引用再退出，避免 Exit 中停机导致重复清理。</summary>
        private void ExitCurrentState(StateChangeReason reason = StateChangeReason.Manual, string ruleName = null,
            bool duringFault = false)
        {
            if (CurrentState != null)
            {
                WriteTrace(FsmTraceKind.Exit, CurrentStateId, true, reason: reason, ruleName: ruleName,
                    duration: ReadDiagnosticTime() - _stateEnteredAt);
                _lastStateId = CurrentStateId;
                _hasLastState = true;
                _pendingCleanup = CurrentState;
                CurrentState = null;
                CurrentStateId = default;
            }
            if (_pendingCleanup is IStateMachineInternals child)
            {
                if (duringFault && child.HasPendingCleanup) return;
                child.ExitFromParent();
            }
            else _pendingCleanup?.Exit();
            _pendingCleanup = null;
        }

        /// <summary>业务异常后尽力清理当前状态，清理异常另存而不覆盖原始异常。</summary>
        private void FailStop()
        {
            IsRunning = false;
            _exitRequested = false;
            ClearRequests();
            if (HasPendingCleanup) return;
            try { ExitCurrentState(duringFault: true); }
            catch (Exception cleanupException) { LastCleanupException = cleanupException; }
        }

        /// <summary>清空排队及等待请求。</summary>
        private void ClearRequests()
        {
            _pendingEvents.Clear();
            ClearTransitionRequests();
        }

        /// <summary>完成或替换等待请求时，同时重置计时与通知标记。</summary>
        private void ClearWaiting(bool cancelled = true)
        {
            if (_waitStarted && _waiting.HasValue)
            {
                ChangeRequest request = _waiting.Value;
                WriteTrace(cancelled ? FsmTraceKind.WaitCancelled : FsmTraceKind.WaitEnded,
                    CurrentStateId, CurrentState != null, request.TargetId, true, request.Reason,
                    request.RuleName, duration: ReadDiagnosticTime() - _waitStartedAt);
            }
            _waiting = null;
            _exitNotified = false;
            _waitStarted = false;
        }

        /// <summary>将退出准备通知转交给当前状态。</summary>
        private void NotifyCurrentExitRequest()
        {
            if (CurrentState is IStateExitHandler handler) handler.OnExitRequested();
        }
    }
}
