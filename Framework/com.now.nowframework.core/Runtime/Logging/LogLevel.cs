namespace NowFrameWork.Core
{
    /// <summary>日志级别。数值越大越严重；Off 用于整体关闭（例如 Release 只保留 Error）。</summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,

        /// <summary>关闭全部输出（IsEnabled 恒为 false）。</summary>
        Off = 5,
    }
}