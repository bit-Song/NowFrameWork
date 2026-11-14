namespace NowFrameWork.Core.Utility
{
    /// <summary>
    /// 确定性哈希。
    ///
    /// 为什么不直接用 `string.GetHashCode()`：它不保证跨进程/跨运行时稳定，
    /// 而池键、地址映射这类场景需要"同一个字符串永远得到同一个值"。
    /// 这里用 FNV-1a（32 位），实现简单、无分配、结果稳定。
    /// </summary>
    public static class Hash
    {
        const int FnvOffsetBasis = unchecked((int)2166136261);
        const int FnvPrime = 16777619;

        public static int Of(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            int hash = FnvOffsetBasis;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= FnvPrime;
            }

            return hash;
        }

        /// <summary>混洗两个哈希值；顺序敏感。</summary>
        public static int Combine(int first, int second)
        {
            int hash = first;
            hash = (hash * 397) ^ second;
            return hash;
        }

        public static int Combine(int first, int second, int third)
            => Combine(Combine(first, second), third);
    }
}