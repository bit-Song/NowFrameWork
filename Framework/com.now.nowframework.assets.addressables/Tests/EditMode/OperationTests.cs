using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace NowFrameWork.Assets.Adapter.Tests
{
    public class OperationTests
    {
        sealed class PendingOperation : AsyncOperationBase<int>
        {
            public int DestroyCalls;
            protected override void Execute() { }
            public void Finish() => Complete(42, true, (string)null);
            public void Fail() => Complete(0, false, "expected failure");
            protected override void Destroy() => DestroyCalls++;
        }

        [Test]
        public void CompletedWait_LeavesReleaseToOwner()
        {
            using (var manager = new ResourceManager())
            {
                var operation = new PendingOperation();
                var handle = manager.StartOperation(operation, default);
                operation.Finish();
                AddressablesOperation.WaitAsync(handle, TimeSpan.Zero, default).GetAwaiter().GetResult();
                Assert.IsTrue(handle.IsValid());
                Assert.AreEqual(0, operation.DestroyCalls);
                AddressablesOperation.Release(handle);
                Assert.AreEqual(1, operation.DestroyCalls);
            }
        }

        [UnityTest]
        public IEnumerator CancelledWait_DoesNotReleaseOrCancelUnderlyingOperation()
        {
            using (var manager = new ResourceManager())
            using (var cts = new CancellationTokenSource())
            {
                var operation = new PendingOperation();
                var handle = manager.StartOperation(operation, default);
                var task = AddressablesOperation.WaitAsync(handle, TimeSpan.Zero, cts.Token).AsTask();
                cts.Cancel();
                while (!task.IsCompleted) yield return null;
                Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                Assert.IsTrue(handle.IsValid());
                Assert.IsFalse(handle.IsDone);
                operation.Finish();
                AddressablesOperation.Release(handle);
                Assert.AreEqual(1, operation.DestroyCalls);
            }
        }

        [UnityTest]
        public IEnumerator Timeout_DoesNotReleaseHandle_AndLateCompletionIsSafe()
        {
            using (var manager = new ResourceManager())
            {
                var operation = new PendingOperation();
                var handle = manager.StartOperation(operation, default);
                var task = AddressablesOperation.WaitAsync(handle, TimeSpan.FromMilliseconds(1), default).AsTask();
                while (!task.IsCompleted) yield return null;
                Assert.Throws<TimeoutException>(() => task.GetAwaiter().GetResult());
                Assert.IsTrue(handle.IsValid());
                Assert.AreEqual(0, operation.DestroyCalls);
                operation.Finish();
                AddressablesOperation.Release(handle);
                Assert.AreEqual(1, operation.DestroyCalls);
            }
        }

        [Test]
        public void PreCancelledCompletedWait_PreservesCancellationType()
        {
            using (var manager = new ResourceManager())
            using (var cts = new CancellationTokenSource())
            {
                var operation = new PendingOperation();
                var handle = manager.StartOperation(operation, default);
                operation.Finish();
                cts.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    AddressablesOperation.WaitAsync(handle, TimeSpan.Zero, cts.Token).GetAwaiter().GetResult());
                AddressablesOperation.Release(handle);
            }
        }

        [Test]
        public void FailedWait_LeavesHandleForSingleOwnerRelease()
        {
            var previous = ResourceManager.ExceptionHandler;
            ResourceManager.ExceptionHandler = (_, __) => { };
            try
            {
                using (var manager = new ResourceManager())
                {
                    var operation = new PendingOperation();
                    var handle = manager.StartOperation(operation, default);
                    operation.Fail();
                    Assert.Catch<Exception>(() => AddressablesOperation.WaitAsync(handle, TimeSpan.Zero, default).GetAwaiter().GetResult());
                    Assert.IsTrue(handle.IsValid());
                    Assert.AreEqual(0, operation.DestroyCalls);
                    AddressablesOperation.Release(handle);
                    // 失败操作还持有引擎的延迟完成引用，需要推进 ResourceManager 才最终销毁。
                    typeof(ResourceManager).GetMethod("Update", System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic).Invoke(manager, new object[] { 0f });
                    Assert.AreEqual(1, operation.DestroyCalls);
                }
            }
            finally { ResourceManager.ExceptionHandler = previous; }
        }
    }
}
