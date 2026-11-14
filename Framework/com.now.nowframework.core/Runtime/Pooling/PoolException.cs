using System;

namespace NowFrameWork.Core
{
    /// <summary>池已满且无闲置对象时抛出——调用方要么扩容，要么改用 TryGet 做软失败。</summary>
    public sealed class PoolExhaustedException : Exception
    {
        public PoolExhaustedException(string message) : base(message) { }
    }

    /// <summary>重复归还、归还非本池对象等使用错误。</summary>
    public sealed class PoolUsageException : Exception
    {
        public PoolUsageException(string message) : base(message) { }
    }
}