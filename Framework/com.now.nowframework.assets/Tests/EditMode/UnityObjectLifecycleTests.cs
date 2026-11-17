using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Assets.Tests
{
    public sealed class UnityObjectLifecycleTests
    {
        [SetUp] public void SetUp() => MainThreadGuard.BindCurrentThread();
        [Test] public void DestroyedObject_DoesNotWait() => Assert.IsTrue(UnityObjectLifecycle.WaitDestroyedAsync(() => true, 0).GetAwaiter().GetResult());
        [Test] public void ExhaustedBudget_DoesNotPretendDestroyed() => Assert.IsFalse(UnityObjectLifecycle.WaitDestroyedAsync(() => false, 0).GetAwaiter().GetResult());
        [Test] public void ConditionBecomesTrueOnLastFrame_Succeeds()
        {
            int frames = 0;
            Assert.IsTrue(UnityObjectLifecycle.WaitDestroyedAsync(() => frames == 3, 3,
                () => { frames++; return UniTask.CompletedTask; }).GetAwaiter().GetResult());
            Assert.AreEqual(3, frames);
        }
    }
}
