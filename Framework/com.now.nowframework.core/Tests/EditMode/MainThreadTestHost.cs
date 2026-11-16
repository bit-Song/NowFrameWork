using NUnit.Framework;
using NowFrameWork.Core.Threading;

[SetUpFixture]
public sealed class CoreMainThreadTestHost
{
    [OneTimeSetUp]
    public void BindHostThread() => MainThreadGuard.BindCurrentThread();
}
