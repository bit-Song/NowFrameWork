using System;

namespace NowFrameWork.Core.Storage
{
    /// <summary>数据完整但当前程序不支持其格式/schema；保留文件并禁止自动回退或覆盖。</summary>
    public sealed class SettingsCompatibilityException : Exception
    {
        public SettingsCompatibilityException(string message) : base(message) { }
    }
}
