using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NowFrameWork.Core.Threading
{
    /// <summary>
    /// 宿主显式登记主线程；Assert 绝不以首次调用者初始化。
    /// Unity 适配包自动登记，纯 .NET 宿主需在启动时调用 BindCurrentThread。
    /// 只诊断线程误用，不提供线程安全、自动调度或误用后的状态恢复。
    /// </summary>
    public static class MainThreadGuard
    {
        static int _threadId;
        public static int ThreadId => Volatile.Read(ref _threadId);
        public static bool IsMainThread => ThreadId != 0 && ThreadId == Environment.CurrentManagedThreadId;

        /// <summary>仅由可信宿主初始化入口调用；相同线程重复登记幂等，拒绝更换主线程。</summary>
        public static void BindCurrentThread()
        {
            int current = Environment.CurrentManagedThreadId;
            int previous = Interlocked.CompareExchange(ref _threadId, current, 0);
            if (previous != 0 && previous != current)
                throw new InvalidOperationException("不能重新登记主线程：main=" + previous + ", current=" + current);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Assert([CallerMemberName] string operation = null)
        {
            int expected = ThreadId;
            int actual = Environment.CurrentManagedThreadId;
            if (expected == 0)
                throw new InvalidOperationException("主线程尚未登记，宿主须先调用 MainThreadGuard.BindCurrentThread：" + operation);
            if (expected != actual)
                throw new InvalidOperationException("仅允许主线程调用 " + operation + "：main=" + expected + ", current=" + actual);
        }
    }
}
