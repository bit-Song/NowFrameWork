using NowFrameWork.Core;

namespace NowFrameWork.Core.Logging
{
    /// <summary>空实现：用于可选日志依赖，避免到处写 null 判断。</summary>
    public static class Logs
    {
        /// <summary>全局共享的空日志：IsEnabled 恒为 false，Write 直接返回，零分配。</summary>
        public static readonly ILog Null = new NullLog();

        sealed class NullLog : ILog
        {
            /// <summary>空日志不输出任何级别。</summary>
            public LogLevel MinLevel => LogLevel.Off;
            /// <summary>空日志始终返回 false。</summary>
            public bool IsEnabled(LogLevel level) => false;
            /// <summary>空实现。</summary>
            public void Write(LogLevel level, string tag, string message) { }
        }
    }
}
