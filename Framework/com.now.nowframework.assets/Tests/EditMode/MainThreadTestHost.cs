using NUnit.Framework;
using NowFrameWork.Core.Threading;

[SetUpFixture]
public sealed class AssetsMainThreadTestHost
{
    [OneTimeSetUp]
    public void BindHostThread() => MainThreadGuard.BindCurrentThread();
}
