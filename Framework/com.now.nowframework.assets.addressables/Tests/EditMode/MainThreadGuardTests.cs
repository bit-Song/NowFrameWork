using System;
using System.Threading;
using NUnit.Framework;

namespace NowFrameWork.Assets.Adapter.Tests
{
    public class MainThreadGuardTests
    {
        static void RejectedOnWorker(Action action, string operation)
        {
            Exception error = null;
            var worker = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
            worker.IsBackground = true;
            worker.Start();
            Assert.IsTrue(worker.Join(3000));
            Assert.That(error, Is.TypeOf<InvalidOperationException>(), error?.ToString());
            StringAssert.Contains(operation, error.Message);
        }

        [Test]
        public void WorkerInitialize_IsRejectedBeforeAddressables()
        {
            var provider = new AddressablesProvider();
            RejectedOnWorker(() => provider.InitializeAsync(default).GetAwaiter().GetResult(), "AddressablesProvider.InitializeAsync");
        }

        [Test]
        public void WorkerSceneLoad_IsRejectedBeforeAddressables()
        {
            var scenes = new AddressablesSceneService();
            RejectedOnWorker(() => scenes.LoadAdditiveAsync("unused", default).GetAwaiter().GetResult(), "AddressablesSceneService.LoadAdditiveAsync");
        }
    }
}
