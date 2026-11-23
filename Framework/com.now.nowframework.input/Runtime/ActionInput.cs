using System;
using System.Collections.Generic;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Input
{
    /// <summary>一次动作采样：连续值 + 本帧按下 + 持续按住。</summary>
    public readonly struct ActionState
    {
        public ActionState(float value, bool pressed, bool held)
            : this(value, pressed, held, default)
        {
        }

        public ActionState(float value, bool pressed, bool held, UnityEngine.Vector2 vector)
        {
            Value = value;
            Pressed = pressed;
            Held = held;
            Vector = vector;
        }

        /// <summary>归一化连续值（摇杆/按键为 0/1）。</summary>
        public float Value { get; }

        /// <summary>本帧刚按下。</summary>
        public bool Pressed { get; }

        /// <summary>当前按住。</summary>
        public bool Held { get; }

        /// <summary>二维方向（摇杆/移动轴）；标量动作时为 default。</summary>
        public UnityEngine.Vector2 Vector { get; }

        public bool IsIdle => Value == 0f && !Pressed && !Held && Vector.sqrMagnitude == 0f;

        public static ActionState Idle => default;

        public override string ToString() => "v=" + Value + (Pressed ? " +pressed" : "") + (Held ? " +held" : "");
    }

    /// <summary>动作来源（键鼠 / 手柄 / 触屏 / 虚拟摇杆）。同一动作可以有多个来源。</summary>
    public interface IActionSource
    {
        /// <summary>读取动作；返回 false 表示该来源没有这个动作。</summary>
        bool TryRead(int actionId, out ActionState state);
    }

    /// <summary>
    /// 动作读取与**输入屏蔽**。
    ///
    /// 职责：
    /// 1. 从多个来源合并同一动作（第一个非空闲的来源获胜）；
    /// 2. 提供屏蔽令牌：UI 模态、输入框聚焦、暂停时压入，屏蔽期间所有动作返回空闲；
    /// 3. 不持有设备、不做映射与改键——映射由项目的 Input Actions 完成。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class ActionInput
    {
        readonly IActionSource[] _sources;
        readonly HashSet<string> _gates = new HashSet<string>(StringComparer.Ordinal);
        int _suppressionCount;

        public ActionInput(params IActionSource[] sources)
        {
            MainThreadGuard.Assert("ActionInput.ctor");

            _sources = sources ?? Array.Empty<IActionSource>();

            for (int i = 0; i < _sources.Length; i++)
                if (_sources[i] == null) throw new ArgumentException("动作来源包含 null", nameof(sources));
        }

        /// <summary>是否有屏蔽令牌存在。</summary>
        public bool IsSuppressed
        {
            get { MainThreadGuard.Assert("ActionInput.IsSuppressed"); return GateCount > 0; }
        }

        /// <summary>当前屏蔽原因数量（诊断用）。</summary>
        public int GateCount => _gates.Count + _suppressionCount;

        /// <summary>
        /// 压入屏蔽令牌；释放后自动解除。可嵌套：全部释放才恢复。
        ///
        /// 典型用法：模态弹窗打开时 `using (input.Suppress("ui.modal")) { ... }`。
        /// </summary>
        public IDisposable Suppress(string reason)
        {
            MainThreadGuard.Assert("ActionInput.Suppress");

            if (reason == null) throw new ArgumentNullException(nameof(reason));

            var gate = new Gate(this);
            _suppressionCount++;
            return gate;
        }

        /// <summary>
        /// 按原因开启/关闭屏蔽（幂等）。
        ///
        /// 适合每帧根据外部状态同步（例如 `SetGate("ui.modal", ui.TopModal.IsValid)`）：
        /// 状态不变时不产生分配，也不会重复计数。
        /// </summary>
        public void SetGate(string reason, bool active)
        {
            MainThreadGuard.Assert("ActionInput.SetGate");

            if (reason == null) throw new ArgumentNullException(nameof(reason));

            if (active) _gates.Add(reason);
            else _gates.Remove(reason);
        }

        /// <summary>读取动作；被屏蔽时返回空闲状态。</summary>
        public ActionState Read(int actionId)
        {
            MainThreadGuard.Assert("ActionInput.Read");

            if (GateCount > 0) return ActionState.Idle;

            for (int i = 0; i < _sources.Length; i++)
            {
                if (!_sources[i].TryRead(actionId, out ActionState state)) continue;
                if (!state.IsIdle) return state;
            }

            return ActionState.Idle;
        }

        public bool WasPressed(int actionId) => Read(actionId).Pressed;

        public float GetValue(int actionId) => Read(actionId).Value;

        public UnityEngine.Vector2 GetVector(int actionId) => Read(actionId).Vector;

        sealed class Gate : IDisposable
        {
            ActionInput _owner;
            public Gate(ActionInput owner) => _owner = owner;

            public void Dispose()
            {
                MainThreadGuard.Assert("ActionInput.Gate.Dispose");
                ActionInput owner = _owner;
                if (owner == null) return;

                _owner = null;
                owner._suppressionCount--;
            }
        }
    }
}
