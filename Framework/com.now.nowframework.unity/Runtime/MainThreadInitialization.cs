using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.Unity
{
    /// <summary>由引擎回调登记线程，支持关闭 Domain Reload；重复回调不重新绑定。</summary>
    internal static class MainThreadInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitializeRuntime() => MainThreadGuard.BindCurrentThread();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InitializeEditor() => MainThreadGuard.BindCurrentThread();
#endif
    }
}
