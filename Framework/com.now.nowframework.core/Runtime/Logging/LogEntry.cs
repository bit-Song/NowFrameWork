using NowFrameWork.Core;

namespace NowFrameWork.Core.Logging
{
    /// <summary>
    /// 一条日志。readonly struct：入参用 <c>in</c> 传递，不装箱、不分配。
    /// 只保存字符串引用，格式化留给 Sink（Core 不依赖 Unity 的字符串处理）。
    /// </summary>
    public readonly struct LogEntry
    {
        public LogEntry(LogLevel level, string tag, string message, double time)
        {
            Level = level;
            Tag = tag;
            Message = message;
            Time = time;
        }

        public LogLevel Level { get; }

        /// <summary>模块或子系统标记，例如 "C01"。注册期就固定，运行期只传引用。</summary>
        public string Tag { get; }

        public string Message { get; }

        /// <summary>由注入的时钟提供；Core 不引用 UnityEngine.Time。</summary>
        public double Time { get; }
    }
}
