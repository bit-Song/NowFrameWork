namespace NowFrameWork.Core
{
    /// <summary>空实现：用于可选日志依赖，避免到处写 null 判断。</summary>
    public static class Logs
    {
        /// <summary>全局共享的空日志：IsEnabled 恒为 false，Write 直接返回，零分配。</summary>
        public static readonly ILog Null = new NullLog();

        sealed class NullLog : ILog
        {
            public LogLevel MinLevel => LogLevel.Off;
            public bool IsEnabled(LogLevel level) => false;
            public void Write(LogLevel level, string tag, string message) { }
        }
    }
}