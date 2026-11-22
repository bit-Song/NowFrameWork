using NowFrameWork.Input;

namespace Game
{
    /// <summary>把虚拟摇杆适配成移动动作（二维）。</summary>
    public sealed class JoystickActionSource : IActionSource
    {
        readonly VirtualJoystick _joystick;
        readonly int _moveAction;

        public JoystickActionSource(VirtualJoystick joystick, int moveAction)
        {
            _joystick = joystick ?? throw new System.ArgumentNullException(nameof(joystick));
            _moveAction = moveAction;
        }

        public bool TryRead(int actionId, out ActionState state)
        {
            if (actionId != _moveAction) { state = default; return false; }

            UnityEngine.Vector2 value = _joystick.Value;

            state = new ActionState(value.magnitude, false, _joystick.IsPressed, value);
            return true;
        }
    }
}