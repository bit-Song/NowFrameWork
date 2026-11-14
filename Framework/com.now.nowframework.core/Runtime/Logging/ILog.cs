using NowFrameWork.Core;

namespace NowFrameWork.Core.Logging
{
    /// <summary>
    /// 日志契约。
    ///
    /// 零分配约定：调用方在拼接消息前**必须先** IsEnabled 判断，例如
    /// <code>
    /// if (log.IsEnabled(LogLevel.Debug)) log.Write(LogLevel.Debug, "C01", "顺序: " + string.Join(" -> ", ids));
    /// </code>
    /// 这样关闭的级别连字符串都不会创建。
    /// </summary>
    public interface ILog
    {
        LogLevel MinLevel { get; }

        /// <summary>级别判断；关闭的级别返回 false，调用方据此跳过一切字符串拼接。</summary>
        bool IsEnabled(LogLevel level);

        /// <summary>写入一条已构造好的消息；message 为 null 时按空串处理。</summary>
        void Write(LogLevel level, string tag, string message);
    }
}
