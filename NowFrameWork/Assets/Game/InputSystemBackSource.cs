using NowFrameWork.Input;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>
    /// 新 Input System 下的返回键来源：Esc / Android 返回键 / 手柄 B。
    ///
    /// Android 的返回键在 Unity 中映射为 Escape；手柄 B 作为可选的第二来源。
    /// 不做动作映射与改键——那是 Input Actions 的职责。
    /// </summary>
    public sealed class InputSystemBackSource : IBackSource
    {
        public bool TryConsumeBack()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;

            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonEast.wasPressedThisFrame;
        }
    }
}