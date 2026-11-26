using NowFrameWork.Core;

namespace NowFrameWork.Core.Diagnostics
{
    /// <summary>一条聚合后的错误记录。同类错误只占一条，用 Count 记录次数。</summary>
    public readonly struct ErrorEntry
    {
        /// <summary>记录一种错误的聚合信息。</summary>
        public ErrorEntry(string module, string message, int count, double firstTime, double lastTime, bool fatal)
        {
            Module = module;
            Message = message;
            Count = count;
            FirstTime = firstTime;
            LastTime = lastTime;
            Fatal = fatal;
        }

        /// <summary>来源模块。</summary>
        public string Module { get; }
        /// <summary>错误消息。</summary>
        public string Message { get; }

        /// <summary>同一 (模块 + 消息) 的累计次数。</summary>
        public int Count { get; }

        /// <summary>首次时间。</summary>
        public double FirstTime { get; }
        /// <summary>最近时间。</summary>
        public double LastTime { get; }

        /// <summary>是否已越过致命阈值。</summary>
        public bool Fatal { get; }

        /// <summary>便于日志输出的一行摘要。</summary>
        public override string ToString()
            => "[" + Module + "] x" + Count + (Fatal ? " (FATAL) " : " ") + Message;
    }
}
