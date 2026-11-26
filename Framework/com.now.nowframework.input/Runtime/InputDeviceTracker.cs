using System;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Input
{
    /// <summary>当前生效的输入设备类型（用于 UI 图标与提示）。</summary>
    public enum InputDeviceKind
    {
        None = 0,
        KeyboardMouse = 1,
        Gamepad = 2,
        Touch = 3,
    }

    /// <summary>
    /// 设备类型去重与通知。
    ///
    /// 设备枚举与事件监听由 Input System 负责（项目侧适配），本类只做：
    /// 1. 类型变化时**只发一次**事件；
    /// 2. 提供当前类型快照。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class InputDeviceTracker
    {
        Action<InputDeviceKind> _changed;

        /// <summary>当前设备类型。</summary>
        public InputDeviceKind Current { get; private set; } = InputDeviceKind.None;

        /// <summary>只在类型真正变化时触发。</summary>
        public event Action<InputDeviceKind> Changed
        {
            add { MainThreadGuard.Assert("InputDeviceTracker.Changed.add"); _changed += value; }
            remove { MainThreadGuard.Assert("InputDeviceTracker.Changed.remove"); _changed -= value; }
        }

        /// <summary>更新当前类型；返回是否发生了变化。</summary>
        public bool Set(InputDeviceKind kind)
        {
            MainThreadGuard.Assert("InputDeviceTracker.Set");

            if (kind == Current) return false;

            Current = kind;
            _changed?.Invoke(kind);
            return true;
        }
    }
}