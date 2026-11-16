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
        public string PersistentDataPath
        {
            get
            {
                MainThreadGuard.Assert("UnityPlatformInfo.PersistentDataPath");
                return Application.persistentDataPath;
            }
        }

        public string StreamingAssetsPath
        {
            get
            {
                MainThreadGuard.Assert("UnityPlatformInfo.StreamingAssetsPath");
                return Application.streamingAssetsPath;
            }
        }

        public string CachePath
        {
            get
            {
                MainThreadGuard.Assert("UnityPlatformInfo.CachePath");
                return Path.Combine(Application.persistentDataPath, "cache");
            }
        }

        // 注意：本类有同名属性 SystemLanguage，枚举必须写全名，否则会被属性遮蔽
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