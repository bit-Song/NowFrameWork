using System;
using NowFrameWork.Core.Fsm;
using UnityEngine;

namespace NowFrameWork.Framework.PlayMode.Tests
{
    /// <summary>仅用于 PlayMode 场景测试的真实 MonoBehaviour 宿主。</summary>
    public sealed class HfsmLifecycleHost : MonoBehaviour
    {
        public enum Scenario { IdleMove, Attack, Nested }

        public sealed class Counters
        {
            public int Enters, Ticks, FixedTicks, Exits, ExitRequests, Faults, AnimationEvents; // 生命周期计数。
        }

        public Scenario Mode; // 激活前选择测试场景。
        public bool MoveRequested; // 测试输入。
        public bool ThrowOnTick; // 是否模拟子状态机故障。
        public float AttackDuration = 0.15f; // 攻击收招时间。
        public readonly Counters Counts = new(); // 销毁后测试仍可保留并检查的计数对象。
        public StateMachine<string> Fsm { get; private set; }
        public StateMachine<string> Child { get; private set; }
        public Exception CaughtException { get; private set; }

        /// <summary>配置场景所需状态，配置阶段不执行行为。</summary>
        private void Awake()
        {
            Fsm = new StateMachine<string> { Name = "SceneRoot" };
            Fsm.Faulted += fault => Counts.Faults++;
            if (Mode == Scenario.Nested)
            {
                Child = new StateMachine<string> { Name = "Child" };
                Child.AddState("Work", new Probe(this, () =>
                {
                    if (ThrowOnTick) throw new InvalidOperationException("Intentional child fault");
                }));
                Child.SetInitialState("Work");
                Fsm.AddState("Alive", Child);
                Fsm.SetInitialState("Alive");
            }
            else if (Mode == Scenario.Attack)
            {
                Fsm.AddState("Attack", new AttackState(this, Fsm));
                Fsm.AddState("Idle", new Probe(this));
                Fsm.AddState("Dead", new Probe(this));
                Fsm.AddGlobalEventTransition("KILLED", "Dead", force: true, priority: 100, name: "强制死亡");
                Fsm.SetInitialState("Attack");
            }
            else
            {
                Fsm.AddState("Idle", new Probe(this));
                Fsm.AddState("Move", new Probe(this, () => transform.position += Vector3.right * Time.deltaTime));
                Fsm.AddTransition("Idle", "Move", () => MoveRequested, name: "开始移动");
                Fsm.AddTransition("Move", "Idle", () => !MoveRequested, name: "停止移动");
                Fsm.AddEventTransition("Move", "ANIMATION_DONE", "Idle", name: "动画完成");
                Fsm.SetInitialState("Idle");
            }
        }

        /// <summary>通过真实启用消息启动。</summary>
        private void OnEnable() { Fsm.Enter(); }
        /// <summary>转发帧更新并保留测试故意触发的异常。</summary>
        private void Update()
        {
            try { Fsm.Tick(); }
            catch (Exception exception) { CaughtException = exception; }
        }
        /// <summary>通过真实物理更新推进。</summary>
        private void FixedUpdate()
        {
            try { Fsm.FixedTick(); }
            catch (Exception exception) { CaughtException = exception; }
        }
        /// <summary>通过真实禁用和销毁路径清理。</summary>
        private void OnDisable() { Fsm?.Exit(); }
        /// <summary>由真正的 AnimationEvent 调用，不在测试中直接模拟调用。</summary>
        public void OnAnimationFinished()
        {
            Counts.AnimationEvents++;
            MoveRequested = false;
            Fsm.SendEvent("ANIMATION_DONE");
        }

        private sealed class Probe : IState
        {
            private readonly HfsmLifecycleHost _owner; // 测试宿主。
            private readonly Action _tick; // 可选业务行为。
            public Probe(HfsmLifecycleHost owner, Action tick = null) { _owner = owner; _tick = tick; }
            public void Enter() { _owner.Counts.Enters++; }
            public void Tick() { _owner.Counts.Ticks++; _tick?.Invoke(); }
            public void FixedTick() { _owner.Counts.FixedTicks++; }
            public void Exit() { _owner.Counts.Exits++; }
        }

        private sealed class AttackState : State<HfsmLifecycleHost, string>
        {
            private float _elapsed; // Unity 时间驱动的收招进度。
            public AttackState(HfsmLifecycleHost owner, StateMachine<string> fsm) : base(owner, fsm) { }
            public override bool CanExit => _elapsed >= Owner.AttackDuration;
            public override string ExitWaitReason => "等待攻击收招";
            public override void Enter() { _elapsed = 0; Owner.Counts.Enters++; }
            public override void Tick() { _elapsed += Time.deltaTime; Owner.Counts.Ticks++; }
            public override void FixedTick() { Owner.Counts.FixedTicks++; }
            public override void OnExitRequested() { Owner.Counts.ExitRequests++; }
            public override void Exit() { Owner.Counts.Exits++; }
        }
    }
}
