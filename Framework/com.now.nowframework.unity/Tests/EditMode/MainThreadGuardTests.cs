using System;
using System.Threading;
using NUnit.Framework;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.Unity.Tests
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
        public void UnityHost_IsBound_AndWorkerTickIsRejectedBeforeUnityTime()
        {
            Assert.IsTrue(MainThreadGuard.IsMainThread);
            var app = App.Create();
            RejectedOnWorker(() => app.Tick(0, 0), "App.Tick");
            Assert.DoesNotThrow(() => app.Tick(0, 0));
            app.ShutdownAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void UnityClockAndPlatform_AreRejectedBeforeNativeApi()
        {
            var clock = new UnityClock();
            var platform = new UnityPlatformInfo();
            RejectedOnWorker(() => { var ignored = clock.Now; }, "UnityClock.Now");
            RejectedOnWorker(() => { var ignored = platform.SystemLanguage; }, "UnityPlatformInfo.SystemLanguage");
            Assert.GreaterOrEqual(clock.Now, 0);
            Assert.IsNotEmpty(platform.SystemLanguage);
        }
    }
}
