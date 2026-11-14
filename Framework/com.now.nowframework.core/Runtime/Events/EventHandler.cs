namespace NowFrameWork.Core
{
    /// <summary>
    /// 事件处理器委托。用 <c>in</c> 传参：大结构体不产生拷贝，也绝不装箱。
    /// 热路径请传静态方法或已缓存的方法组，不要用捕获局部变量的 lambda（R3）。
    /// </summary>
    public delegate void EventHandler<T>(in T evt) where T : struct;
}