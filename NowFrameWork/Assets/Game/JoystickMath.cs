using UnityEngine;

namespace Game
{
    /// <summary>摇杆取值：死区、归一化与方向钳制。纯函数，便于单测。</summary>
    public static class JoystickMath
    {
        /// <summary>
        /// 把指针位移换算成 [-1,1] 的方向与 0..1 的强度。
        /// </summary>
        /// <param name="delta">指针相对摇杆中心的位移。</param>
        /// <param name="radius">摇杆半径（像素）。</param>
        /// <param name="deadzone">死区比例 0..1，小于该比例的位移视为 0。</param>
        public static Vector2 Evaluate(Vector2 delta, float radius, float deadzone)
        {
            if (radius <= 0f) return Vector2.zero;

            float magnitude = delta.magnitude / radius;   // 0..∞
            if (magnitude <= Mathf.Clamp01(deadzone)) return Vector2.zero;

            if (magnitude > 1f) magnitude = 1f;

            // 死区外重新映射到 0..1，避免从死区边界突然跳到最大
            float dz = Mathf.Clamp01(deadzone);
            float strength = dz >= 1f ? 0f : (magnitude - dz) / (1f - dz);

            return delta.normalized * strength;
        }
    }
}