using System;

namespace NowFrameWork.Core.Platform
{
    /// <summary>应用生命周期状态。移动端与桌面端共用一套，由平台层翻译。</summary>
    public enum AppState
    {
        Starting = 0,
        Foreground = 1,
        Background = 2,
        Quitting = 3,
    }

    /// <summary>
    /// 生命周期契约。
    ///
    /// 关键约束：**信号必须幂等**。
    /// 移动端同一次切后台可能连发 pause + focus-lost，桌面端可能只发 focus。
    /// 若不去重，业务会在一次切换里收到两次"暂停"，导致重复暂停音频/计时。
    /// </summary>
    public interface IAppLifecycle
    {
        AppState State { get; }

        /// <summary>状态真正发生变化时触发；重复信号不触发。</summary>
        event Action<AppState> StateChanged;

        void NotifyPause();
        void NotifyResume();

        /// <summary>退出：只生效一次，之后忽略其它信号。</summary>
        void NotifyQuit();
    }

    /// <summary>默认实现：四状态机 + 幂等去重。</summary>
    public sealed class AppLifecycle : IAppLifecycle
    {
        AppState _state = AppState.Starting;

        public AppState State => _state;

        public event Action<AppState> StateChanged;

        public void NotifyPause()
        {
            // Starting 视为前台：第一次信号是 pause 时直接进后台，不先发一次 Foreground
            if (_state == AppState.Foreground || _state == AppState.Starting) SetState(AppState.Background);
        }

        public void NotifyResume()
        {
            if (_state == AppState.Starting || _state == AppState.Background) SetState(AppState.Foreground);
        }

        public void NotifyQuit()
        {
            if (_state == AppState.Quitting) return;
            SetState(AppState.Quitting);
        }

        void SetState(AppState next)
        {
            if (_state == next) return;

            _state = next;
            StateChanged?.Invoke(next);
        }
    }
}