using System.Threading;

namespace NowFrameWork.Input
{
    /// <summary>一次返回请求最终由谁处理。</summary>
    public enum BackRoute
    {
        None = 0,
        Ui = 1,
        Fallback = 2,
        Unhandled = 3,
    }

    /// <summary>
    /// 返回意图的来源（Esc / Android 返回键 / 手柄 B / 虚拟按钮）。
    ///
    /// 约定：`TryConsumeBack` 返回 true 表示"本帧有一次返回意图"，并**消费掉**它，
    /// 避免同一次按键被多个来源重复上报。
    /// </summary>
    public interface IBackSource
    {
        bool TryConsumeBack();
    }

    /// <summary>
    /// UI 没消费返回时的兜底（例如"再按一次退出"）。
    ///
    /// 返回 true 表示已处理；框架不会擅自退出应用。
    /// </summary>
    public interface IBackFallback
    {
        bool OnBack();
    }
}