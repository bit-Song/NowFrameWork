using System;

namespace NowFrameWork.Core.Storage
{
    /// <summary>恢复不会自动写回。PrimaryError 非空时，宿主应上报原始主文件损坏原因。</summary>
    public readonly struct SettingsLoadResult
    {
        /// <summary>设置实际来源（主文件/备份/默认值等）。</summary>
        public SettingsLoadSource Source { get; }
        /// <summary>文件中记录的版本号。</summary>
        public int StoredVersion { get; }
        /// <summary>是否执行过迁移。</summary>
        public bool Migrated { get; }
        /// <summary>主文件读取失败的原因（无错误为 null）。</summary>
        public Exception PrimaryError { get; }

        /// <summary>由存储实现构造的加载结果。</summary>
        internal SettingsLoadResult(SettingsLoadSource source, int storedVersion, bool migrated, Exception primaryError)
        {
            Source = source;
            StoredVersion = storedVersion;
            Migrated = migrated;
            PrimaryError = primaryError;
        }
    }
}
