using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NowFrameWork.Core.Fsm
{
    /// <summary>跨泛型 ID 类型执行配置校验及异常传播。</summary>
    internal interface IStateMachineInternals
    {
        void TickFromParent();
        void FixedTickFromParent();
        void ExitFromParent();
        bool HasPendingCleanup { get; }
        string GetFaultStatePath();
        void CollectConfigurationErrors(List<string> errors, string path);
        void InvalidateConfiguration();
        void StopFromChildFault(Exception exception, string faultPath);
    }

    /// <summary>供不同 ID 类型的父子状态机统一清除恢复历史。</summary>
    internal interface IHistoryReset
    {
        void ResetHistory();
    }

    /// <summary>检查状态构造时绑定的状态机，避免注册到错误宿主。</summary>
    internal interface IBoundState
    {
        IStateMachineInfo BoundMachine { get; }
    }

    /// <summary>状态机重新进入时选择哪个直接子状态。</summary>
    public enum StateEntryMode { InitialState, LastState }

    /// <summary>进入状态的原因，用于通知和历史记录。</summary>
    public enum StateChangeReason { Initial, History, Manual, LocalTransition, GlobalTransition, Reenter, Restart, LocalEvent, GlobalEvent }

    /// <summary>活动父子链因业务异常停止后的只读通知。</summary>
    public readonly struct FsmFault
    {
        public readonly Exception Exception; // 原始业务异常。
        public readonly string Path; // 故障发生时的层级路径。
        public FsmFault(Exception exception, string path) { Exception = exception; Path = path; }
    }

    /// <summary>一次已完成 Enter 的状态切换记录。</summary>
    public readonly struct StateChange<TStateId>
    {
        public readonly bool HasPrevious; // 首次进入没有旧状态，不能把默认 ID 当成旧状态。
        public readonly TStateId PreviousId; // 切换前的状态 ID。
        public readonly TStateId CurrentId; // 已进入的状态 ID。
        public readonly StateChangeReason Reason; // 本次进入的原因。
        public readonly string RuleName; // 触发规则名称，手动切换时可为空。

        /// <summary>保存一次状态切换的数据。</summary>
        public StateChange(bool hasPrevious, TStateId previousId, TStateId currentId, StateChangeReason reason, string ruleName = null)
        {
            HasPrevious = hasPrevious;
            PreviousId = previousId;
            CurrentId = currentId;
            Reason = reason;
            RuleName = ruleName;
        }
    }

    /// <summary>不依赖具体 ID 类型的只读观察接口。</summary>
    public interface IStateMachineInfo : IState, IStateExitHandler
    {
        long DebugId { get; } // 不同泛型状态机之间也唯一的调试实例标识。
        bool TraceEnabled { get; set; } // 编辑器默认记录，普通玩家构建默认关闭。
        long TraceVersion { get; } // 原始轨迹增量采集版本。
        double CurrentStateSeconds { get; } // 当前状态的真实停留时间，仅调试查询时计算。
        string Name { get; set; } // 观察器中显示的名字。
        bool IsRunning { get; } // 是否正在运行。
        bool HasPendingTransition { get; } // 是否在等待退出或有切换排队。
        IState CurrentState { get; } // 当前直接子状态。
        IStateMachineInfo Parent { get; } // 注册本状态机的父状态机。
        Exception LastException { get; } // 最近一次业务回调异常。
        Exception LastCleanupException { get; } // 异常停机时附带的清理异常。
        Exception LastObserverException { get; } // 最近一次观察者异常，不影响状态运行。
        Exception LastDiagnosticsException { get; } // 诊断时钟异常，不影响状态运行。
        bool HasPendingCleanup { get; } // 是否需要由根状态机重试退出。
        bool IsFaulted { get; } // 是否因业务异常停止。
        string FaultPath { get; } // 最近故障发生时的活动路径。
        string PendingTarget { get; } // 等待进入的直接子状态 ID 文本。
        string PendingRuleName { get; } // 等待中的转移名称。
        double ExitWaitSeconds { get; } // 已等待退出的真实时间，不受 timeScale 影响。
        double ExitWaitWarningSeconds { get; set; } // 等待告警阈值，0 表示关闭提示。
        bool IsExitWaitOverdue { get; } // 是否超过告警阈值，仅诊断不自动打断。
        string ExitWaitReason { get; } // 当前阻塞退出的原因。
        string LastUnhandledEvent { get; } // 最近一次未被当前状态接收的事件名。

        /// <summary>检查整棵状态树，返回全部配置错误，不执行生命周期。</summary>
        List<string> ValidateConfiguration();

        /// <summary>获取当前活动状态路径，仅在调试时调用。</summary>
        string GetActivePath();

        /// <summary>获取最近的切换记录文本，仅在调试时调用。</summary>
        string GetHistoryText();

        /// <summary>清除恢复历史并重新启动。</summary>
        void Restart(bool force = true);

        /// <summary>将指定版本之后的轨迹复制到文本快照，返回最新版本。</summary>
        long CopyTraceTo(List<FsmTraceRecord> destination, long afterVersion);
    }

    /// <summary>维护状态实例归属，弱引用避免注册表延长父状态机的生命周期。</summary>
    internal static class StateOwnership
    {
        private sealed class Registration
        {
            public readonly WeakReference<IStateMachineInfo> Owner; // 管理该状态的状态机。
            public Registration(IStateMachineInfo owner) { Owner = new WeakReference<IStateMachineInfo>(owner); }
        }

        private static readonly ConditionalWeakTable<IState, Registration> Owners = new(); // 按实例识别归属。

        /// <summary>查找状态所属的仍然存活的父状态机。</summary>
        public static IStateMachineInfo GetOwner(IState state)
        {
            return Owners.TryGetValue(state, out Registration registration) &&
                registration.Owner.TryGetTarget(out IStateMachineInfo owner) ? owner : null;
        }

        /// <summary>注册唯一归属，同时禁止父子循环与运行中子状态机被接管。</summary>
        public static void Register(IState state, IStateMachineInfo owner)
        {
            if (state is IBoundState bound && !ReferenceEquals(bound.BoundMachine, owner))
                throw new ArgumentException("状态构造时绑定的状态机与注册目标不一致。", nameof(state));
            if (GetOwner(state) != null)
                throw new ArgumentException("该状态实例已经被注册，请为每个位置创建独立实例。", nameof(state));
            for (IStateMachineInfo ancestor = owner; ancestor != null; ancestor = ancestor.Parent)
                if (ReferenceEquals(ancestor, state))
                    throw new ArgumentException("不能建立循环嵌套关系。", nameof(state));
            if (state is IStateMachineInfo machine && machine.IsRunning)
                throw new InvalidOperationException("请先停止子状态机，再将其注册到父状态机。");
            Owners.Remove(state);
            Owners.Add(state, new Registration(owner));
        }
    }

    /// <summary>供观察器获取状态机，使用弱引用，不持有角色资源。</summary>
    public static class FsmDiagnostics
    {
        private static readonly List<WeakReference<IStateMachineInfo>> Machines = new(); // 构造过的状态机。

        /// <summary>登记状态机，仅由框架构造函数调用。</summary>
        [Conditional("UNITY_EDITOR")]
        internal static void Register(IStateMachineInfo machine)
        {
            // 定期回收失效登记，未打开观察器时也不会无限累积弱引用。
            if (Machines.Count % 64 == 0)
                for (int i = Machines.Count - 1; i >= 0; i--)
                    if (!Machines[i].TryGetTarget(out _)) Machines.RemoveAt(i);
            Machines.Add(new WeakReference<IStateMachineInfo>(machine));
        }

        /// <summary>获取存活状态机的快照，并移除失效引用；只在调试界面调用。</summary>
        public static List<IStateMachineInfo> GetMachines()
        {
            var result = new List<IStateMachineInfo>(); // 供本次界面绘制使用的快照。
            for (int i = Machines.Count - 1; i >= 0; i--)
            {
                if (Machines[i].TryGetTarget(out IStateMachineInfo machine))
                    result.Add(machine);
                else
                    Machines.RemoveAt(i);
            }
            return result;
        }
    }
}
