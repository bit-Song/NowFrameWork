namespace NowFrameWork.Core.Platform
{
    /// <summary>
    /// 平台信息契约：路径与系统语言。
    /// 只放"业务真的会用到"的：存档目录、只读资源目录、缓存目录、系统语言。
    /// 设备型号、分辨率等放 Diagnostics，不进契约（否则换平台就要改接口）。
    /// </summary>
    public interface IPlatformInfo
    {
        /// <summary>可写目录（存档、日志、崩溃转储）。</summary>
        string PersistentDataPath { get; }

        /// <summary>只读随包资源目录。</summary>
        string StreamingAssetsPath { get; }

        /// <summary>可清理的缓存目录（下载缓存、缩略图）。</summary>
        string CachePath { get; }

        /// <summary>系统语言，BCP-47 风格（如 "zh-CN"、"en-US"）。</summary>
        string SystemLanguage { get; }
    }
}