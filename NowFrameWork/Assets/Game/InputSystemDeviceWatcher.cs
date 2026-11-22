using NowFrameWork.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game
{
    /// <summary>
    /// 设备类型监听：把 Input System 的"最近按下的设备"与"设备插拔"翻译成 <see cref="InputDeviceKind"/>。
    ///
    /// 判断策略：
    /// 1. 任意控件按下 → 采用该控件所属设备类型（键鼠/手柄/触屏）；
    /// 2. 设备移除/断开 → 按剩余设备回退（手柄 > 键鼠 > 触屏）。
    ///
    /// 只做通知，不做设备配对；配对请用 `InputUser` / `PlayerInputManager`。
    /// </summary>
    public sealed class InputSystemDeviceWatcher : MonoBehaviour
    {
        public readonly InputDeviceTracker Tracker = new InputDeviceTracker();

        void OnEnable()
        {
            InputSystem.onEvent += OnInputEvent;
            InputSystem.onDeviceChange += OnDeviceChange;

            RefreshFromAvailable();
        }

        void OnDisable()
        {
            InputSystem.onEvent -= OnInputEvent;
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        /// <summary>任意设备事件都视为"最近使用的设备"；只处理状态事件，忽略布局/连接等控制事件。</summary>
        void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device == null) return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;

            Tracker.Set(KindOf(device));
        }

        static InputDeviceKind KindOf(InputDevice device)
        {
            if (device is Gamepad) return InputDeviceKind.Gamepad;
            if (device is Touchscreen) return InputDeviceKind.Touch;
            if (device is Keyboard || device is Mouse) return InputDeviceKind.KeyboardMouse;

            return InputDeviceKind.None;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                RefreshFromAvailable();
        }

        /// <summary>按剩余设备回退；当前类型仍可用时保持不动。</summary>
        public void RefreshFromAvailable()
        {
            if (Tracker.Current == InputDeviceKind.Gamepad && Gamepad.all.Count > 0) return;
            if (Tracker.Current == InputDeviceKind.KeyboardMouse && Keyboard.current != null) return;

            if (Gamepad.all.Count > 0) Tracker.Set(InputDeviceKind.Gamepad);
            else if (Keyboard.current != null || Mouse.current != null) Tracker.Set(InputDeviceKind.KeyboardMouse);
            else if (Touchscreen.current != null) Tracker.Set(InputDeviceKind.Touch);
        }
    }
}