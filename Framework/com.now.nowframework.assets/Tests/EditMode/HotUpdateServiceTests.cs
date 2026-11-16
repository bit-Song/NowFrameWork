using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace NowFrameWork.Assets.Tests
{
    public class HotUpdateServiceTests
    {
        sealed class Provider : IAssetUpdateProvider
        {
            public bool Changed = true;
            public bool Updated;
            public bool FailUpdate;
            public bool FailDownload;
            public readonly List<string> Calls = new List<string>();
            public UniTask<bool> CheckForUpdatesAsync(CancellationToken ct) { Calls.Add("check"); return UniTask.FromResult(Changed); }
            public UniTask<int> UpdateCatalogsAsync(CancellationToken ct)
            {
                Calls.Add("catalog");
                if (FailUpdate) throw new InvalidOperationException("update failed");
                Updated = true;
                return UniTask.FromResult(1);
            }
            public UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct)
            {
                Calls.Add("size");
                if (Changed && !Updated) throw new InvalidOperationException("queried old catalog");
                return UniTask.FromResult(2048L);
            }
            public UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct)
            {
                Calls.Add("download");
                if (Changed && !Updated || FailDownload) throw new InvalidOperationException("download failed");
                progress?.Report(1f);
                return UniTask.CompletedTask;
            }
        }
        [Test]
        public void Prepare_UpdatesCatalogBeforeSizeAndDownload()
        {
            var provider = new Provider();
            var service = new HotUpdateService(provider);
            var plan = service.PrepareAsync("dll").GetAwaiter().GetResult();
            service.DownloadAsync("dll").GetAwaiter().GetResult();
            Assert.IsTrue(plan.HasUpdate);
            Assert.AreEqual(2048, plan.DownloadBytes);
            CollectionAssert.AreEqual(new[] { "check", "catalog", "size", "download" }, provider.Calls);
        }
        [Test]
        public void Prepare_NoCatalogUpdate_StillMeasuresMissingCache()
        {
            var provider = new Provider { Changed = false };
            var plan = new HotUpdateService(provider).PrepareAsync("dll").GetAwaiter().GetResult();
            Assert.IsFalse(plan.HasUpdate);
            Assert.AreEqual(2048, plan.DownloadBytes);
            CollectionAssert.AreEqual(new[] { "check", "size" }, provider.Calls);
        }
        [Test]
        public void Check_DoesNotActivateCatalog()
        {
            var provider = new Provider();
            Assert.IsTrue(new HotUpdateService(provider).CheckAsync().GetAwaiter().GetResult());
            CollectionAssert.AreEqual(new[] { "check" }, provider.Calls);
        }
        [Test]
        public void Download_WithoutPreparedMatchingKey_IsRejected()
        {
            var service = new HotUpdateService(new Provider());
            Assert.Throws<InvalidOperationException>(() => service.DownloadAsync("dll").GetAwaiter().GetResult());
            service.PrepareAsync("dll").GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => service.DownloadAsync("other").GetAwaiter().GetResult());
        }
        [Test]
        public void PrepareFailure_ResetsState_AndCanRetry()
        {
            var provider = new Provider { FailUpdate = true };
            var service = new HotUpdateService(provider);
            Assert.Throws<InvalidOperationException>(() => service.PrepareAsync("dll").GetAwaiter().GetResult());
            Assert.AreEqual(HotUpdateState.Idle, service.State);
            Assert.Throws<InvalidOperationException>(() => service.DownloadAsync("dll").GetAwaiter().GetResult());
            provider.FailUpdate = false;
            service.PrepareAsync("dll").GetAwaiter().GetResult();
            service.DownloadAsync("dll").GetAwaiter().GetResult();
        }
        [Test]
        public void DownloadFailure_CanRetryPreparedPlan()
        {
            var provider = new Provider { FailDownload = true };
            var service = new HotUpdateService(provider);
            service.PrepareAsync("dll").GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => service.DownloadAsync("dll").GetAwaiter().GetResult());
            Assert.AreEqual(HotUpdateState.Idle, service.State);
            provider.FailDownload = false;
            service.DownloadAsync("dll").GetAwaiter().GetResult();
        }
    }
}
