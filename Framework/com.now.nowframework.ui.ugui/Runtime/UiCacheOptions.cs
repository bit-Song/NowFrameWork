using System;

namespace NowFrameWork.UI
{
    /// <summary>模块级空闲实例预算。只有 CacheOnClose 的面板参与缓存。</summary>
    public sealed class UiCacheOptions
    {
        internal static readonly UiCacheOptions Default = new UiCacheOptions();

        public UiCacheOptions(int capacity = 16, TimeSpan? idleTimeout = null)
        {
            // 活动会话至多 384，加上缓存后仍在默认工厂的 512 条跟踪额度内。
            if (capacity < 0 || capacity > 128) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(60);
            if (IdleTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        }

        public int Capacity { get; }
        public TimeSpan IdleTimeout { get; }
    }
}
