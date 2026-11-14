using NowFrameWork.Core;

namespace NowFrameWork.Core.Diagnostics
{
    /// <summary>一条聚合后的错误记录。同类错误只占一条，用 Count 记录次数。</summary>
    public readonly struct ErrorEntry
    {
        public ErrorEntry(string module, string message, int count, double firstTime, double lastTime, bool fatal)
        {
            Module = module;
            Message = message;
            Count = count;
            FirstTime = firstTime;
            LastTime = lastTime;
            Fatal = fatal;
        }

        public string Module { get; }
        public string Message { get; }

        /// <summary>同一 (模块 + 消息) 的累计次数。</summary>
        public int Count { get; }

        public double FirstTime { get; }
        public double LastTime { get; }

        /// <summary>是否已越过致命阈值。</summary>
        public bool Fatal { get; }

        public override string ToString()
            => "[" + Module + "] x" + Count + (Fatal ? " (FATAL) " : " ") + Message;
    }
}
