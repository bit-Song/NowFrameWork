using System;

namespace NowFrameWork.Core.Utility
{
    /// <summary>
    /// 参数与前置条件校验。抛标准异常（ArgumentNullException / ArgumentOutOfRangeException），
    /// 便于调用方按类型处理，也便于日志与堆栈定位。
    ///
    /// 只放**实际用到**的检查：这不是一个校验框架。
    /// </summary>
    public static class Guard
    {
        public static T NotNull<T>(T value, string name) where T : class
        {
            if (value == null) throw new ArgumentNullException(name);
            return value;
        }

        public static string NotNullOrEmpty(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("不能为空", name);
            return value;
        }

        public static int Positive(int value, string name)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(name, value, "必须大于 0");
            return value;
        }

        public static int NotNegative(int value, string name)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(name, value, "不能为负");
            return value;
        }

        public static float InRange(float value, float min, float max, string name)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(name, value, "必须在 [" + min + ", " + max + "] 之间");
            return value;
        }

        /// <summary>断言式检查；用于"理论上不可能发生"的分支。</summary>
        public static void That(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}