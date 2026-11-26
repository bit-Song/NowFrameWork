using System;

using NowFrameWork.Core;

namespace NowFrameWork.Core.Time
{
    /// <summary>
    /// 计时器句柄。带版本号：槽位被复用后，旧句柄的 Cancel 会安全地失败，不会误杀新计时器。
    /// </summary>
    public readonly struct TimerHandle : IEquatable<TimerHandle>
    {
        /// <summary>指向计时器槽位与版本的句柄。</summary>
        internal TimerHandle(int index, int version)
        {
            Index = index;
            Version = version;
        }

        /// <summary>槽位下标。</summary>
        internal int Index { get; }
        /// <summary>槽位版本，防止复用误取消。</summary>
        internal int Version { get; }

        /// <summary>是否为非默认构造出的句柄（default 的 Version 为 0）。</summary>
        public bool IsValid => Version > 0;

        public bool Equals(TimerHandle other) => Index == other.Index && Version == other.Version;

        public override bool Equals(object obj) => obj is TimerHandle other && Equals(other);

        public override int GetHashCode() => (Index * 397) ^ Version;

        public override string ToString() => "Timer(" + Index + ":" + Version + ")";
    }
}
