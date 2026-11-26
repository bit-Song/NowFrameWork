using UnityEngine;

namespace NowFrameWork.Input
{
    /// <summary>
    /// 旧输入系统下的返回键来源（Esc）。
    ///
    /// Android 的返回键在 Unity 中同样映射为 `KeyCode.Escape`。
    /// 项目使用新 Input System 时，请改用单独的 `Unity.InputSystem` 适配源。
    /// </summary>
    public sealed class LegacyBackSource : IBackSource
    {
        /// <summary>旧输入系统返回键是否按下（一帧一次）。</summary>
        public bool TryConsumeBack()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }
    }
}