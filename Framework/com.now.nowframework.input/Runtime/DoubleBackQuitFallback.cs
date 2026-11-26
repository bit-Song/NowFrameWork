using System;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.Input
{
    /// <summary>
    /// "再按一次退出"兜底：第一次返回只提示，窗口内再按一次才执行退出。
    ///
    /// 行为：
    /// 1. 首次返回：消费掉（避免退出应用），进入提示状态；
    /// 2. 窗口内再次返回：执行 `quit`；
    /// 3. 超过窗口：重新开始提示，不退出。
    ///
    /// 时间源可注入，便于 EditMode 测试；默认使用 `Time.unscaledTime`（暂停中也能计时）。
    /// </summary>
    public sealed class DoubleBackQuitFallback : IBackFallback
    {
        readonly float _windowSeconds;
        readonly Func<float> _now;
        readonly Action _quit;

        float _deadline = float.NegativeInfinity;

        /// <summary>创建双击退出兜底；可注入时间与退出动作。</summary>
        public DoubleBackQuitFallback(float windowSeconds = 2f, Action quit = null, Func<float> now = null)
        {
            if (windowSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(windowSeconds));

            _windowSeconds = windowSeconds;
            _quit = quit ?? DefaultQuit;
            _now = now ?? DefaultNow;
        }

        /// <summary>是否处于"再按一次"的提示状态。</summary>
        public bool IsPrompting => _now() <= _deadline;

        /// <summary>提示剩余秒数；不在提示中时为 0。</summary>
        public float RemainingSeconds
        {
            // 读取 Time.unscaledTime，只能在主线程访问
            get
            {
                float left = _deadline - _now();
                return left > 0f ? left : 0f;
            }
        }

        /// <summary>处理一次返回；窗口内再次返回则退出。</summary>
        public bool OnBack()
        {
            MainThreadGuard.Assert("DoubleBackQuitFallback.OnBack");

            float now = _now();

            if (now <= _deadline)
            {
                _deadline = float.NegativeInfinity;
                _quit();
                return true;
            }

            // 第一次：消费掉，进入提示窗口
            _deadline = now + _windowSeconds;
            return true;
        }

        static float DefaultNow() => Time.unscaledTime;

        static void DefaultQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}