using NowFrameWork.Core;

namespace NowFrameWork.Core.Startup
{
    /// <summary>
    /// 启动进度接收者。由 UI（加载条）或日志实现；框架只负责上报。
    ///
    /// 约定：
    /// 1. `Progress` 的取值归一化到 0..1，且单调不减；
    /// 2. `Phase` 表示进入新阶段，允许同一阶段内多次 `Progress`；
    /// 3. 启动失败时回调 `Failed`，参数带上阶段名与模块 Id——
    ///    这是「每阶段失败可定位」的落点，避免只拿到一个没有上下文的异常。
    ///
    /// 实现方不得抛异常：它在启动链路上被同步调用。
    /// </summary>
    public interface IStartupProgress
    {
        /// <summary>进入新阶段，例如 "Initialize" / "Start"。</summary>
        void Phase(string name);

        /// <summary>归一化进度 0..1。</summary>
        void Progress(float value01);

        /// <summary>启动失败：phase 为失败阶段，moduleId 为出错模块（可能为空）。</summary>
        void Failed(string phase, string moduleId);
    }
}
