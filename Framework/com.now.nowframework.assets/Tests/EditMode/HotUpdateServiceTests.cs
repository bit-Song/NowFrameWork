using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace NowFrameWork.Assets.Tests
{
    /// <summary>热更流程：状态机、检查/下载/应用的编排。</summary>
    public class HotUpdateServiceTests
    {
        sealed class FloatProgress : IProgress<float>
        {
            public float Value;
            public void Report(float value) => Value = value;
        }

        sealed class FakeUpdateProvider : IAssetUpdateProvider
        {
            public bool HasUpdate;
            public long Size = 1024;
            public int CheckCalls;
            public int SizeCalls;
            public int DownloadCalls;
            public int ApplyCalls;
            public bool ThrowOnCheck;

            public UniTask<bool> CheckForUpdatesAsync(CancellationToken ct)
            {
                CheckCalls++;
                if (ThrowOnCheck) throw new InvalidOperationException("check failed");
                return UniTask.FromResult(HasUpdate);
            }

            public UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct)
            {
                SizeCalls++;
                return UniTask.FromResult(Size);
            }

            public UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct)
            {
                DownloadCalls++;
                progress?.Report(1f);
                return UniTask.CompletedTask;
            }

            public UniTask<int> UpdateCatalogsAsync(CancellationToken ct)
            {
                ApplyCalls++;
                return UniTask.FromResult(2);
            }
        }

        [Test]
        public void Check_NoUpdate_DoesNotQuerySize()
        {
            var provider = new FakeUpdateProvider { HasUpdate = false };
            var service = new HotUpdateService(provider);

            HotUpdatePlan plan = service.CheckAsync("catalog").GetAwaiter().GetResult();

            Assert.IsFalse(plan.HasUpdate);
            Assert.AreEqual(0, plan.DownloadBytes);
            Assert.AreEqual(1, provider.CheckCalls);
            Assert.AreEqual(0, provider.SizeCalls, "无更新时不应查询下载大小");
            Assert.AreEqual(HotUpdateState.Idle, service.State);
        }

        [Test]
        public void Check_WithUpdate_ReturnsSize()
        {
            var provider = new FakeUpdateProvider { HasUpdate = true, Size = 2048 };
            var service = new HotUpdateService(provider);

            HotUpdatePlan plan = service.CheckAsync("catalog").GetAwaiter().GetResult();

            Assert.IsTrue(plan.HasUpdate);
            Assert.AreEqual(2048, plan.DownloadBytes);
            Assert.AreEqual(1, provider.SizeCalls);
        }

        [Test]
        public void Check_Throws_ResetsState()
        {
            var provider = new FakeUpdateProvider { ThrowOnCheck = true };
            var service = new HotUpdateService(provider);

            Assert.Throws<InvalidOperationException>(() =>
                service.CheckAsync("catalog").GetAwaiter().GetResult());

            Assert.AreEqual(HotUpdateState.Idle, service.State, "异常后状态必须复位，否则永远卡住");
        }

        [Test]
        public void Download_And_Apply_DelegateToProvider()
        {
            var provider = new FakeUpdateProvider();
            var service = new HotUpdateService(provider);

            var progress = new FloatProgress();

            service.DownloadAsync("catalog", progress).GetAwaiter().GetResult();
            Assert.AreEqual(1, provider.DownloadCalls);
            Assert.AreEqual(1f, progress.Value);

            int applied = service.ApplyAsync().GetAwaiter().GetResult();
            Assert.AreEqual(2, applied);
            Assert.AreEqual(1, provider.ApplyCalls);
            Assert.AreEqual(HotUpdateState.Idle, service.State);
        }
    }
}