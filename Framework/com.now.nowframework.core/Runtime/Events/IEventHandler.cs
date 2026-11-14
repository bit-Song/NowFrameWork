namespace NowFrameWork.Core
{
    /// <summary>
    /// 无委托的事件处理器。高频事件用它可彻底避免订阅期的委托分配。
    /// 注意：Handle 用 <c>in</c> 传参，因此泛型参数是**不变**的（by-ref 参数不允许逆变）。
    /// </summary>
    public interface IEventHandler<T> where T : struct
    {
        void Handle(in T evt);
    }
}