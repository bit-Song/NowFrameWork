namespace NowFrameWork.UI
{
    public enum UiOpenStatus
    {
        Ok = 0,
        AlreadyOpen = 1,
        Busy = 2,
    }

    /// <summary>打开结果：只有在 Ok / AlreadyOpen 时 Handle 才有效。</summary>
    public readonly struct UiOpenResult
    {
        public UiOpenResult(UiOpenStatus status, UiHandle handle)
        {
            Status = status;
            Handle = handle;
        }

        public UiOpenStatus Status { get; }

        public UiHandle Handle { get; }

        public bool IsOpen => Status == UiOpenStatus.Ok || Status == UiOpenStatus.AlreadyOpen;

        public override string ToString() => Status + (IsOpen ? " " + Handle : "");
    }
}