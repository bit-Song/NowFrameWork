using System;

namespace NowFrameWork.Core.Fsm
{
    /// <typeparam name="TStateId">所属状态机使用的状态标识类型。</typeparam>
    public class State<TStateId> : IState, IStateExitHandler, IExitWaitDiagnostics, IBoundState
    {
        /// <summary>所属状态机，子类通过它请求切换状态。</summary>
        protected readonly StateMachine<TStateId> StateMachine;

        /// <summary>供注册校验使用，确保状态只装配到构造时绑定的状态机。</summary>
        IStateMachineInfo IBoundState.BoundMachine => StateMachine;

        /// <summary>保存所属状态机，供子状态请求切换。</summary>
        /// <param name="stateMachine">管理本状态的状态机实例。</param>
        protected State(StateMachine<TStateId> stateMachine)
        {
            StateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        }

        /// <summary>默认允许退出；子类可重写为收招时间、动画结束等条件。</summary>
        public virtual bool CanExit => true;

        /// <summary>可重写为“等待收招”或“等待动画结束”等具体原因，仅供调试查询。</summary>
        public virtual string ExitWaitReason => "等待当前状态允许退出（CanExit）";

        /// <summary>普通退出被阻塞时调用一次，子类可开始准备退出。</summary>
        public virtual void OnExitRequested() { }

        /// <summary>进入状态时调用，子类可重写初始化行为。</summary>
        public virtual void Enter() { }

        /// <summary>每帧调用，子类可重写持续行为。</summary>
        public virtual void Tick() { }

        /// <summary>物理更新时调用，子类可重写物理行为。</summary>
        public virtual void FixedTick() { }

        /// <summary>退出状态时调用，子类可重写清理行为。</summary>
        public virtual void Exit() { }
    }

    /// <summary>持有强类型宿主的状态基类，供角色行为直接访问宿主能力。</summary>
    /// <typeparam name="TOwner">宿主类型，例如 PlayerControl 或角色能力接口。</typeparam>
    /// <typeparam name="TStateId">所属状态机使用的状态标识类型。</typeparam>
    public class State<TOwner, TStateId> : State<TStateId>
        where TOwner : class
    {
        /// <summary>创建状态时传入的宿主，每个角色应持有独立状态实例。</summary>
        protected TOwner Owner { get; }

        /// <summary>保存宿主和所属状态机，不执行生命周期回调。</summary>
        /// <param name="owner">本状态操作的宿主对象。</param>
        /// <param name="stateMachine">管理本状态的状态机。</param>
        protected State(TOwner owner, StateMachine<TStateId> stateMachine)
            : base(stateMachine ?? throw new ArgumentNullException(nameof(stateMachine)))
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }
    }
}
