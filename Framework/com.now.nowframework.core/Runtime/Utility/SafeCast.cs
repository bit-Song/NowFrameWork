namespace NowFrameWork.Core.Utility
{
    /// <summary>
    /// 安全转换：把"强转失败会抛异常"变成"返回 false / null"。
    /// 用于外部数据（配置、存档、网络）这类不可信来源。
    /// </summary>
    public static class SafeCast
    {
        public static bool TryCast<T>(object source, out T result) where T : class
        {
            result = source as T;
            return result != null;
        }

        /// <summary>转换失败返回 null；调用方必须处理 null。</summary>
        public static T As<T>(object source) where T : class => source as T;
    }
}