namespace NowFrameWork.UI
{
    /// <summary>无参数界面的共享参数实例（引用类型，避免结构体的泛型实例化）。</summary>
    public sealed class EmptyArgs
    {
        public static readonly EmptyArgs Default = new EmptyArgs();

        EmptyArgs()
        {
        }
    }
}