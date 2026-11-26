using NowFrameWork.Core.Threading;
using System.IO;
using NowFrameWork.Core.Platform;
using UnityEngine;

namespace NowFrameWork.Unity
{
    /// <summary>
    /// 平台信息：把 UnityEngine 的路径与语言翻译成 Core 契约。
    /// 三平台差异（Android 沙盒、iOS 沙盒、Windows 用户目录）由 Application 统一处理。
    /// </summary>
    public sealed class UnityPlatformInfo : IPlatformInfo
    {
        /// <summary>可写持久化目录。</summary>
        public string PersistentDataPath
        {
            get
            {
                MainThreadGuard.Assert("UnityPlatformInfo.PersistentDataPath");
                return Application.persistentDataPath;
            }
        }

        /// <summary>只读流式资源目录。</summary>
        public string StreamingAssetsPath
        {
            get
            {
                MainThreadGuard.Assert("UnityPlatformInfo.StreamingAssetsPath");
                return Application.streamingAssetsPath;
            }
        }

        /// <summary>临时缓存目录。</summary>
        public string CachePath
        {
            get
            {
                MainThreadGuard.Assert("UnityPlatformInfo.CachePath");
                return Path.Combine(Application.persistentDataPath, "cache");
            }
        }

        // 注意：本类有同名属性 SystemLanguage，枚举必须写全名，否则会被属性遮蔽
        /// <summary>系统语言代码。</summary>
        public string SystemLanguage
        {
            get
            {
                MainThreadGuard.Assert("UnityPlatformInfo.SystemLanguage");
                return UnityEngine.Application.systemLanguage switch
                {
                    UnityEngine.SystemLanguage.ChineseSimplified => "zh-CN",
                    UnityEngine.SystemLanguage.ChineseTraditional => "zh-TW",
                    UnityEngine.SystemLanguage.English => "en-US",
                    UnityEngine.SystemLanguage.Japanese => "ja-JP",
                    UnityEngine.SystemLanguage.Korean => "ko-KR",
                    _ => "en-US",
                };
            }
        }
    }
}