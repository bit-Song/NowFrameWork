using System;

namespace NowFrameWork.Core.Storage
{
    /// <summary>恢复不会自动写回。PrimaryError 非空时，宿主应上报原始主文件损坏原因。</summary>
    public readonly struct SettingsLoadResult
    {
        public SettingsLoadSource Source { get; }
        public int StoredVersion { get; }
        public bool Migrated { get; }
        public Exception PrimaryError { get; }

        internal SettingsLoadResult(SettingsLoadSource source, int storedVersion, bool migrated, Exception primaryError)
        {
            Source = source;
            StoredVersion = storedVersion;
            Migrated = migrated;
            PrimaryError = primaryError;
        }
    }
}
