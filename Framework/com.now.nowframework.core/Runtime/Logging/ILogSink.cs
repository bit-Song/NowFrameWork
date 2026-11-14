using NowFrameWork.Core;

namespace NowFrameWork.Core.Logging
{
    /// <summary>
    /// 日志出口（Console、文件、远端）。实现放在平台包（如 unity），Core 只定义契约。
    /// </summary>
    public interface ILogSink
    {
        /// <summary>该 Sink 是否关心这个级别；返回 false 时 Logger 不会调用 Write。</summary>
        bool IsEnabled(LogLevel level);

        /// <summary>写入一条日志；实现不得抛异常（否则会影响业务调用点）。</summary>
        void Write(in LogEntry entry);
    }
}
