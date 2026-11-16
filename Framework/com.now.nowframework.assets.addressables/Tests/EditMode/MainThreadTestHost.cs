using NUnit.Framework;
using NowFrameWork.Core.Threading;

[SetUpFixture]
public sealed class AddressablesMainThreadTestHost
{
    [OneTimeSetUp]
    public void BindHostThread() => MainThreadGuard.BindCurrentThread();
}
