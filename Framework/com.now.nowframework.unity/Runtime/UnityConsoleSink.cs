using NowFrameWork.Core.Logging;

using NowFrameWork.Core;

namespace NowFrameWork.Unity
{
    /// <summary>
    /// 控制台出口：把 LogEntry 映射到 Debug.Log / LogWarning / LogError。
    /// 这是整个工程里唯一直接调用 UnityEngine.Debug 的日志代码。
    ///
    /// 注意：Debug.Log 本身会构造字符串，所以它只应在**已启用**的级别上被调用；
    /// 过滤由 Logger 与 IsEnabled 双层保证。
    /// </summary>
    public sealed class UnityConsoleSink : ILogSink
    {
        public bool IsEnabled(LogLevel level) => level != LogLevel.Off;

        public void Write(in LogEntry entry)
        {
            string message = Format(in entry);

            switch (entry.Level)
            {
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(message);
                    break;
                case LogLevel.Warn:
                    UnityEngine.Debug.LogWarning(message);
                    break;
                default:
                    UnityEngine.Debug.Log(message);
                    break;
            }
        }

        static string Format(in LogEntry entry)
        {
            // tag 为空的模块直接输出消息，省一次拼接
            if (string.IsNullOrEmpty(entry.Tag)) return entry.Message;
            return "[" + entry.Tag + "] " + entry.Message;
        }
    }
}
