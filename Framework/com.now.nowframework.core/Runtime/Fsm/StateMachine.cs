using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Core.Fsm
{
    /// <summary>可嵌套的状态机。所有更新及配置应在同一线程执行。</summary>
    /// <typeparam name="TStateId">状态标识类型，例如枚举或字符串。</typeparam>
    public partial class StateMachine<TStateId> : IStateMachineInfo, IHistoryReset, IStateMachineInternals, IExitWaitDiagnostics
    {
        private const int MaxTransitionsPerPass = 128; // 一次回调及其后续请求的切换上限。
        private const int HistoryCapacity = 64; // 最多保留的切换记录数。
        private readonly Dictionary<TStateId, IState> _states = new(); // 状态 ID 到独立实例。
        private readonly Dictionary<TStateId, List<Transition>> _localTransitions = new(); // 各来源状态的局部规则。
        private readonly List<Transition> _globalTransitions = new(); // 公共规则，按优先级降序，同值按注册顺序。
        private readonly Dictionary<TStateId, Dictionary<string, List<Transition>>> _localEventTransitions = new(); // 局部事件规则。
        private readonly Dictionary<string, List<Transition>> _globalEventTransitions = new(StringComparer.Ordinal); // 公共事件规则。
        private readonly Queue<string> _pendingEvents = new(); // 当前状态机收到的事件，不隐式广播给其他状态机。
        private readonly ObserverList<StateChange<TStateId>> _stateObservers = new(); // 订阅变更时创建快照。
        private readonly ObserverList<FsmFault> _faultObservers = new(); // 停机后的故障通知。
        private readonly FsmTraceBuffer<TStateId> _trace = new(); // 有界轨迹，记录时不创建 ID 字符串。
        private double _stateEnteredAt; // 当前状态开始进入时刻，用于统计停留时间。
        private double _lastDiagnosticTime;
        private IState _pendingCleanup;
        private static readonly Func<bool> AlwaysTrue = () => true; // 无附加条件的事件共用此委托。
        private static readonly Func<double> DefaultClock = ReadClock; // 默认单调时钟。
        private readonly Func<double> _clock; // 诊断时钟，可注入以便确定性测试。
        private readonly Queue<ChangeRequest> _requests = new(); // 回调返回后执行的切换请求。
        private readonly List<StateChange<TStateId>> _history = new(); // 有限长度的切换历史。
        private readonly Action _tickCallback; // 缓存的每帧更新委托。
        private readonly Action _fixedTickCallback; // 缓存的物理更新委托。
        private readonly Action _enterCallback; // 缓存的进入委托。
        private readonly Action _emptyCallback; // 仅处理请求时使用的缓存委托。
        private readonly Action _exitRequestCallback; // 向当前子状态转发退出请求。
        private ChangeRequest? _waiting; // 正在等待当前状态允许退出的请求。
        private bool _exitNotified; // 当前等待请求是否已经通知状态准备退出。
        private bool _executing; // 是否正在执行生命周期或处理请求。
        private bool _evaluatingCondition; // 条件判断中禁止启停及切换，避免副作用。
        private bool _exitRequested; // 是否需要在当前回调返回后停机。
        private bool _hasInitialState; // 是否显式配置了初始状态。
        private bool _hasLastState; // 是否保存了上次退出时的状态。
        private TStateId _initialStateId; // 默认启动状态。
        private TStateId _lastStateId; // 最近退出的直接子状态，用于历史恢复。
        private long _requestSequence; // 用于识别被回调替换的等待请求。
        private int _transitionsThisPass; // 一次 Execute 中累计的切换次数。
        private StateEntryMode _entryMode; // 默认重新进入初始状态。
        private bool _configurationValidated; // 配置变更后失效，下次启动重新检查整棵树。
        private bool _waitStarted; // 是否已开始等待 CanExit。
        private double _waitStartedAt; // 本次等待开始时刻。
        private double _exitWaitWarningSeconds = 5; // 默认五秒后显示等待提示。
        private int _eventsThisPass; // 一次 Execute 内处理的事件数量，用于限制循环。

        private readonly struct Transition
        {
            public readonly TStateId TargetId; // 目标状态。
            public readonly Func<bool> Condition; // 无副作用的条件判断。
            public readonly bool Force; // 是否忽略退出条件并覆盖旧请求。
            public readonly int Priority; // 数值越大越优先，同值保持注册顺序。
            public readonly string Name; // 调试显示用的规则名称。
            public Transition(TStateId targetId, Func<bool> condition, bool force, int priority, string name)
            { TargetId = targetId; Condition = condition; Force = force; Priority = priority; Name = name; }
        }

        private readonly struct ChangeRequest
        {
            public readonly TStateId TargetId; // 目标状态。
            public readonly bool Force; // 是否强制打断。
            public readonly bool Reenter; // 是否允许重复进入同一 ID。
            public readonly bool ResetHistory; // 是否清除整棵子树的恢复历史。
            public readonly StateChangeReason Reason; // 调试用切换原因。
            public readonly long Sequence; // 本次请求的唯一序号。
            public readonly string RuleName; // 触发本请求的命名规则。
            public ChangeRequest(TStateId id, bool force, bool reenter, bool resetHistory,
                StateChangeReason reason, long sequence, string ruleName)
            { TargetId = id; Force = force; Reenter = reenter; ResetHistory = resetHistory; Reason = reason; Sequence = sequence; RuleName = ruleName; }
        }

        /// <summary>观察窗口中的名称，可按角色命名。</summary>
        public string Name { get; set; } = "FSM";
        /// <summary>本运行域内唯一的调试标识，不参与业务寻址。</summary>
        public long DebugId { get; } = FsmTraceSequence.NextMachineId();
        /// <summary>编辑器默认启用；正式玩家构建默认关闭，可按需手动启用。</summary>
        public bool TraceEnabled { get; set; }
#if UNITY_EDITOR
            = true;
#endif
        /// <summary>当前原始轨迹版本，没有新记录时无需重复复制。</summary>
        public long TraceVersion => _trace.Version;
        /// <summary>当前状态的真实停留秒数，停止后为零，不逐帧累加。</summary>
        public double CurrentStateSeconds => CurrentState != null ? Math.Max(0, ReadDiagnosticTime() - _stateEnteredAt) : 0;

        /// <summary>是否正在运行，请求停机后立即变为 false。</summary>
        public bool IsRunning { get; private set; }
        /// <summary>当前直接子状态；停止或执行旧状态 Exit 时为 null。</summary>
        public IState CurrentState { get; private set; }
        /// <summary>当前状态 ID，仅在 CurrentState 非空时有效。</summary>
        public TStateId CurrentStateId { get; private set; }
        /// <summary>注册本状态机的父状态机，根状态机为 null。</summary>
        public IStateMachineInfo Parent => StateOwnership.GetOwner(this);
        /// <summary>是否有排队或等待退出的请求。</summary>
        public bool HasPendingTransition => _waiting.HasValue || _requests.Count > 0;
        /// <summary>当前子状态是否允许正常退出，嵌套状态机会递归查询。</summary>
        public bool CanExit
        {
            get
            {
                if (!IsRunning || !(CurrentState is IStateExitHandler handler)) return true;
                try { return handler.CanExit; }
                catch (Exception exception) { StopWithFault(exception); throw; }
            }
        }
        /// <summary>最近一次业务异常，下一次 Enter 时清空。</summary>
        public Exception LastException { get; private set; }
        /// <summary>异常清理时附带的异常，不覆盖原始业务异常。</summary>
        public Exception LastCleanupException { get; private set; }
        /// <summary>观察者抛出的最近一次异常，不影响业务状态推进。</summary>
        public Exception LastObserverException { get; private set; }
        public Exception LastDiagnosticsException { get; private set; }
        public bool HasPendingCleanup => _pendingCleanup != null;
        /// <summary>是否因业务异常停止，显式 Enter/Restart 后清空。</summary>
        public bool IsFaulted => !IsRunning && LastException != null;
        /// <summary>清理前捕获的完整活动路径。</summary>
        public string FaultPath { get; private set; }
        /// <summary>最近未匹配的事件名称；仅作诊断，不自动警告刷屏。</summary>
        public string LastUnhandledEvent { get; private set; }
        /// <summary>等待进入的目标状态文本，仅诊断查询时构造。</summary>
        public string PendingTarget => _waiting.HasValue ? Convert.ToString(_waiting.Value.TargetId) : null;
        /// <summary>等待规则的名称，未命名时显示请求来源。</summary>
        public string PendingRuleName => _waiting.HasValue ? _waiting.Value.RuleName ?? _waiting.Value.Reason.ToString() : null;
        /// <summary>退出等待的真实秒数，不受 Unity timeScale 影响。</summary>
        public double ExitWaitSeconds => _waitStarted ? Math.Max(0, ReadDiagnosticTime() - _waitStartedAt) : 0;
        /// <summary>超过阈值仅显示诊断提示，不自动改变业务状态；0 为关闭提示。</summary>
        public double ExitWaitWarningSeconds
        {
            get => _exitWaitWarningSeconds;
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _exitWaitWarningSeconds = value;
            }
        }
        /// <summary>当前退出等待是否超过告警阈值。</summary>
        public bool IsExitWaitOverdue => _waitStarted && ExitWaitWarningSeconds > 0 && ExitWaitSeconds >= ExitWaitWarningSeconds;
        /// <summary>递归显示叶子状态提供的等待原因。</summary>
        public string ExitWaitReason => CurrentState is IExitWaitDiagnostics diagnostics
            ? diagnostics.ExitWaitReason : "等待当前状态允许退出（CanExit）";
        /// <summary>最近的状态进入记录；只在切换时写入。</summary>
        public IReadOnlyList<StateChange<TStateId>> History { get; }
        /// <summary>每次 Enter 完成后通知；观察者异常单独记录。</summary>
        public event Action<StateChange<TStateId>> StateChanged
        {
            add => _stateObservers.Add(value);
            remove => _stateObservers.Remove(value);
        }
        /// <summary>故障停机后的通知；回调内不自动重启，应在异常返回后显式恢复。</summary>
        public event Action<FsmFault> Faulted
        {
            add => _faultObservers.Add(value);
            remove => _faultObservers.Remove(value);
        }

        /// <summary>重新进入时使用初始状态或上次状态；停止时可配置。</summary>
        public StateEntryMode EntryMode
        {
            get => _entryMode;
            set
            {
                EnsureConfigurable();
                if (value != StateEntryMode.InitialState && value != StateEntryMode.LastState)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _entryMode = value;
            }
        }

        /// <summary>一次性创建更新委托，并登记只读观察信息。</summary>
        public StateMachine(Func<double> clock = null)
        {
            MainThreadGuard.Assert("StateMachine.ctor");
            _clock = clock ?? DefaultClock;
            _tickCallback = TickCurrentState;
            _fixedTickCallback = FixedTickCurrentState;
            _enterCallback = EnterInitialState;
            _emptyCallback = Empty;
            _exitRequestCallback = NotifyCurrentExitRequest;
            History = _history.AsReadOnly();
            FsmDiagnostics.Register(this);
        }

        /// <summary>注册唯一状态实例；重复 ID、共享实例及循环嵌套会报错。</summary>
        /// <param name="id">状态的唯一标识。</param>
        /// <param name="state">状态或子状态机实例。</param>
        public void AddState(TStateId id, IState state)
        {
            EnsureConfigurable();
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (_states.ContainsKey(id)) throw new ArgumentException($"状态 ID 已注册：{id}", nameof(id));
            StateOwnership.Register(state, this);
            _states.Add(id, state);
        }

        /// <summary>只配置初始状态，不执行状态逻辑。</summary>
        /// <param name="id">已注册的状态 ID。</param>
        public void SetInitialState(TStateId id)
        {
            EnsureConfigurable();
            GetState(id);
            _initialStateId = id;
            _hasInitialState = true;
        }






        /// <summary>显式校验整棵树，包含当前未激活的子状态机；不执行生命周期。</summary>
        public List<string> ValidateConfiguration()
        {
            var errors = new List<string>(); // 校验属于配置阶段，不在每帧创建。
            ((IStateMachineInternals)this).CollectConfigurationErrors(errors, Name);
            return errors;
        }

        /// <summary>按 EntryMode 启动；已启动时不重复进入。</summary>
        public void Enter()
        {
            EnsureNotEvaluating();
            if (IsRunning) return;
            if (_executing) throw new InvalidOperationException("请用 Restart 在回调中请求重启。");
            EnsureCleanupComplete();
            if (!_hasInitialState) throw new InvalidOperationException("请先配置初始状态。");
            ValidateBeforeEnter();
            IStateMachineInfo parent = Parent; // 子状态机只允许在父级进入自己的阶段启动。
            if (parent != null && (!parent.IsRunning || !ReferenceEquals(parent.CurrentState, this)))
                throw new InvalidOperationException("已注册的子状态机应由父状态机进入。");
            ClearRequests();
            _exitRequested = false;
            LastException = null;
            LastCleanupException = null;
            LastObserverException = null;
            LastDiagnosticsException = null;
            FaultPath = null;
            LastUnhandledEvent = null;
            IsRunning = true;
            Execute(_enterCallback);
        }

        /// <summary>请求切换；普通请求等待 CanExit，强制请求覆盖旧请求并立即打断。</summary>
        /// <param name="id">目标状态 ID。</param>
        /// <param name="force">是否忽略退出条件。</param>
        /// <param name="reenter">是否允许退出后重新进入同一状态。</param>
        public void ChangeState(TStateId id, bool force = false, bool reenter = false)
        {
            EnsureNotEvaluating();
            if (!IsRunning) return;
            RequestChange(id, force, reenter, false,
                reenter ? StateChangeReason.Reenter : StateChangeReason.Manual);
        }

        /// <summary>请求重新进入当前状态；等待退出条件，或通过 force 强制执行。</summary>
        public void ReenterCurrentState(bool force = false)
        {
            EnsureNotEvaluating();
            if (IsRunning && CurrentState != null) ChangeState(CurrentStateId, force, true);
        }

        /// <summary>清除整棵子树的恢复历史并回到初始状态；回调中也可以安全调用。</summary>
        /// <param name="force">运行时是否强制打断当前状态，默认强制。</param>
        public void Restart(bool force = true)
        {
            EnsureNotEvaluating();
            if (!_hasInitialState) throw new InvalidOperationException("请先配置初始状态。");
            if (IsRunning)
                RequestChange(_initialStateId, force, true, true, StateChangeReason.Restart);
            else
            {
                if (_executing) throw new InvalidOperationException("停机清理期间不能重启。");
                EnsureCleanupComplete();
                EnsureRoot();
                ResetHistoryTree();
                Enter();
            }
        }

        /// <summary>检查公共及局部规则，再更新活动状态；退出等待期间仍推进当前行为。</summary>
        public void Tick()
        {
            EnsureRoot();
            TickOwned();
        }

        void IStateMachineInternals.TickFromParent() => TickOwned();

        private void TickOwned()
        {
            if (IsRunning && !_executing) Execute(_tickCallback);
        }

        /// <summary>更新物理逻辑并尝试完成待处理切换，不重复检查条件规则。</summary>
        public void FixedTick()
        {
            EnsureRoot();
            FixedTickOwned();
        }

        void IStateMachineInternals.FixedTickFromParent() => FixedTickOwned();

        private void FixedTickOwned()
        {
            if (IsRunning && !_executing) Execute(_fixedTickCallback);
        }


        void IStateMachineInternals.ExitFromParent() => ExitOwned();




        string IStateMachineInternals.GetFaultStatePath()
        {
            IState state = CurrentState ?? _pendingCleanup;
            if (state == null) return null;
            string id = Convert.ToString(CurrentState != null ? CurrentStateId : _lastStateId);
            string childPath = (state as IStateMachineInternals)?.GetFaultStatePath();
            return childPath == null ? id : id + "/" + childPath;
        }


        /// <summary>进入初始或历史状态，由缓存委托调用。</summary>
        private void EnterInitialState()
        {
            bool restore = EntryMode == StateEntryMode.LastState && _hasLastState;
            CurrentStateId = restore ? _lastStateId : _initialStateId;
            CurrentState = GetState(CurrentStateId);
            _stateEnteredAt = ReadDiagnosticTime();
            WriteTrace(FsmTraceKind.Enter, default, false, CurrentStateId, true,
                restore ? StateChangeReason.History : StateChangeReason.Initial);
            CurrentState.Enter();
            PublishChange(false, default, CurrentStateId,
                restore ? StateChangeReason.History : StateChangeReason.Initial);
        }

        /// <summary>先处理高层转移，确保被打断的子状态不继续执行 Tick。</summary>
        private void TickCurrentState()
        {
            bool matched = CheckTransitions(_globalTransitions, StateChangeReason.GlobalTransition);
            if (!matched && CurrentState != null &&
                _localTransitions.TryGetValue(CurrentStateId, out List<Transition> local))
                CheckTransitions(local, StateChangeReason.LocalTransition);
            DrainRequests();
            if (!IsRunning) return;
            if (CurrentState is IStateMachineInternals child) child.TickFromParent();
            else CurrentState?.Tick();
        }

        /// <summary>执行物理更新，回调结束后由 Execute 处理请求。</summary>
        private void FixedTickCurrentState()
        {
            if (CurrentState is IStateMachineInternals child) child.FixedTickFromParent();
            else CurrentState?.FixedTick();
        }

        /// <summary>无额外逻辑，仅触发一次受保护的请求处理。</summary>
        private static void Empty() { }




        /// <summary>统一保护回调、请求处理和退出清理，保留原始业务异常。</summary>
        private void Execute(Action callback)
        {
            _executing = true;
            _transitionsThisPass = 0;
            _eventsThisPass = 0;
            try { callback(); DrainRequests(); }
            catch (Exception exception)
            {
                StopWithFault(exception);
                throw;
            }
            finally { _executing = false; }
        }








        /// <summary>通过内部接口清除子树历史，兼容不同 ID 类型的嵌套。</summary>
        void IHistoryReset.ResetHistory() { ResetHistoryTree(); }








        /// <summary>启动前检查整棵树；配置不变时使用成功校验的缓存。</summary>
        private void ValidateBeforeEnter()
        {
            if (_configurationValidated) return;
            List<string> errors = ValidateConfiguration();
            if (errors.Count > 0) throw new InvalidOperationException("状态机配置错误：\n" + string.Join("\n", errors));
            _configurationValidated = true;
        }

        /// <summary>跨 ID 类型递归校验未激活的子状态机。</summary>
        void IStateMachineInternals.CollectConfigurationErrors(List<string> errors, string path)
        {
            if (_states.Count == 0) errors.Add(path + "：没有注册状态。");
            if (!_hasInitialState) errors.Add(path + "：没有配置初始状态。");
            foreach (KeyValuePair<TStateId, IState> pair in _states)
                if (pair.Value is IStateMachineInternals child)
                    child.CollectConfigurationErrors(errors, path + "/" + Convert.ToString(pair.Key));
        }

        /// <summary>任意子配置变更会让父链的成功校验缓存失效。</summary>
        void IStateMachineInternals.InvalidateConfiguration()
        {
            _configurationValidated = false;
            (Parent as IStateMachineInternals)?.InvalidateConfiguration();
        }

        /// <summary>显式策略：子状态机故障停止整条活动父链，不自动恢复或切换到猜测的状态。</summary>
        void IStateMachineInternals.StopFromChildFault(Exception exception, string faultPath)
        {
            if (IsRunning) StopWithFault(exception, faultPath);
        }

        /// <summary>清理活动链并保留故障位置；重复传播同一异常不会重复清理或通知。</summary>
        private void StopWithFault(Exception exception, string faultPath = null)
        {
            if (ReferenceEquals(LastException, exception) && !IsRunning && FaultPath != null) return;
            if (HasPendingCleanup && LastException != null)
            {
                LastCleanupException = exception;
                return;
            }
            if (faultPath == null)
            {
                IStateMachineInfo root = this;
                while (root.Parent != null) root = root.Parent;
                string statePath = (root as IStateMachineInternals)?.GetFaultStatePath();
                faultPath = root.Name + "/" + (statePath ?? root.GetActivePath());
            }
            LastException = exception;
            FaultPath = faultPath;
            TStateId faultStateId = CurrentState != null ? CurrentStateId : _lastStateId;
            WriteTrace(FsmTraceKind.Fault, faultStateId, CurrentState != null || HasPendingCleanup, detail: exception.Message);
            bool wasExecuting = _executing; // 外部直接推进子状态机时也保护停机回调。
            _executing = true;
            try
            {
                FailStop();
                (Parent as IStateMachineInternals)?.StopFromChildFault(exception, faultPath);
                Exception observerError = _faultObservers.Invoke(new FsmFault(exception, faultPath));
                if (observerError != null) LastObserverException = observerError;
            }
            finally { _executing = wasExecuting; }
        }


        /// <summary>按 ID 查找状态，配置错误直接报错。</summary>
        private IState GetState(TStateId id)
        {
            if (!_states.TryGetValue(id, out IState state))
                throw new KeyNotFoundException($"未注册状态 ID：{id}");
            return state;
        }

        /// <summary>只允许在整条所属层级都停止且不在回调中时配置。</summary>
        private void EnsureConfigurable()
        {
            MainThreadGuard.Assert("StateMachine.Configure");
            EnsureCleanupComplete();
            if (IsRunning || _executing) throw new InvalidOperationException("请在状态机停止后配置。");
            for (IStateMachineInfo parent = Parent; parent != null; parent = parent.Parent)
                if (parent.IsRunning) throw new InvalidOperationException("请先停止父状态机再修改子状态机配置。");
            ((IStateMachineInternals)this).InvalidateConfiguration();
        }

        /// <summary>禁止条件函数修改本状态机。</summary>
        private void EnsureNotEvaluating()
        {
            MainThreadGuard.Assert("StateMachine.Mutate");
            if (_evaluatingCondition) throw new InvalidOperationException("转移条件必须只读，不能在条件中启停或切换状态。");
        }

        private void EnsureCleanupComplete()
        {
            if (HasPendingCleanup) throw new InvalidOperationException("请先在根状态机重试退出清理。");
        }

        private void EnsureRoot()
        {
            MainThreadGuard.Assert("StateMachine.Drive");
            if (Parent != null) throw new InvalidOperationException("已注册的子状态机应由根状态机驱动。");
        }

    }
}
