using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace NowFrameWork.Assets.Tests
{
    /// <summary>资源服务引用计数：A-01 ~ A-07 的核心行为。</summary>
    public class AssetServiceTests
    {
        sealed class FakeAsset : ScriptableObject
        {
        }

        sealed class FakeProvider : IAssetProvider
        {
            public int ResolveCalls;
            public int LoadCalls;
            public int ReleaseCalls;
            public Func<AssetKey, string> Canonical = k => k.Address;
            public Func<string, UniTask<ProviderHandle>> Load = null;
            public Action<ProviderHandle> OnRelease;

            public FakeProvider()
            {
                Load = key => UniTask.FromResult(new ProviderHandle(key, ScriptableObject.CreateInstance<FakeAsset>()));
            }

            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
            {
                ResolveCalls++;
                return UniTask.FromResult(Canonical(key));
            }

            public UniTask<ProviderHandle> LoadAsync(string canonicalKey, CancellationToken ct)
            {
                LoadCalls++;
                return Load(canonicalKey);
            }

            public void Release(ProviderHandle handle)
            {
                ReleaseCalls++;
                OnRelease?.Invoke(handle);
            }
        }

        static FakeAsset Asset(ProviderHandle h) => (FakeAsset)h.Asset;

        [Test]
        public void Acquire_LoadsOnce_AndReturnsLease()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);

            var awaiter = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter();
            AssetLease<FakeAsset> lease = awaiter.GetResult();

            Assert.IsNotNull(lease);
            Assert.IsNotNull(lease.Asset);
            Assert.AreEqual(1, provider.LoadCalls);
            Assert.AreEqual(1, service.ActiveLeases);
            lease.Dispose();
        }

        [Test]
        public void SameKey_TwoLeases_OneLoad()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);

            AssetLease<FakeAsset> a = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();
            AssetLease<FakeAsset> b = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();

            Assert.AreNotSame(a, b, "每次申请应得到独立租约");
            Assert.AreSame(a.Asset, b.Asset, "底层资源只有一份");
            Assert.AreEqual(1, provider.LoadCalls);
            Assert.AreEqual(2, service.ActiveLeases);

            a.Dispose();
            b.Dispose();
        }

        [Test]
        public void ReleaseOne_KeepsHandle_ReleaseAll_Releases()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);

            AssetLease<FakeAsset> a = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();
            AssetLease<FakeAsset> b = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();

            a.Dispose();
            Assert.AreEqual(0, provider.ReleaseCalls, "还有租约时不能释放底层");
            Assert.AreEqual(1, service.ActiveLeases);

            b.Dispose();
            Assert.AreEqual(1, provider.ReleaseCalls, "最后一个租约释放后才归还底层");
            Assert.AreEqual(0, service.ActiveLeases);
        }

        [Test]
        public void ReleaseTwice_IsIdempotent()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);

            AssetLease<FakeAsset> lease = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();
            lease.Dispose();
            lease.Dispose();

            Assert.AreEqual(1, provider.ReleaseCalls, "重复释放不应重复归还");
            Assert.AreEqual(0, service.ActiveLeases);
        }

        [Test]
        public void FailedLastRelease_RetainsLeaseAndRetriesSameHandle()
        {
            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            var handle = new ProviderHandle("enemy", asset);
            var provider = new FakeProvider { Load = _ => UniTask.FromResult(handle) };
            provider.OnRelease = released =>
            {
                Assert.AreSame(handle, released);
                if (provider.ReleaseCalls == 1) throw new InvalidOperationException("release failed");
            };
            var service = new AssetService(provider);
            try
            {
                var lease = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();
                Assert.Throws<InvalidOperationException>(() => lease.Dispose());

                Assert.IsFalse(lease.IsReleased);
                Assert.AreEqual(1, service.ActiveLeases);
                Assert.IsTrue(service.Validate(out string error), error);
                Assert.Throws<InvalidOperationException>(() => { _ = lease.Asset; });
                Assert.Throws<InvalidOperationException>(() => service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult());
                Assert.AreEqual(1, provider.LoadCalls);

                lease.Dispose();
                lease.Dispose();
                Assert.IsTrue(lease.IsReleased);
                Assert.AreEqual(2, provider.ReleaseCalls);
                Assert.AreEqual(0, service.ActiveLeases);
                Assert.IsTrue(service.Validate(out error), error);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void FailedUnclaimedHandleRelease_IsRetriedBeforeReload()
        {
            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            var unclaimed = new ProviderHandle("enemy", asset);
            var provider = new FakeProvider();
            provider.Load = key => UniTask.FromResult(provider.LoadCalls == 1 ? unclaimed : new ProviderHandle(key, asset));
            provider.OnRelease = released =>
            {
                if (provider.ReleaseCalls <= 2) Assert.AreSame(unclaimed, released);
                if (provider.ReleaseCalls == 1) throw new InvalidOperationException("release failed");
            };
            var service = new AssetService(provider);
            try
            {
                Assert.Throws<InvalidOperationException>(() => service.AcquireAsync<GameObject>("enemy", default).GetAwaiter().GetResult());
                Assert.AreEqual(0, service.ActiveLeases);
                Assert.AreEqual(1, provider.ReleaseCalls);

                var lease = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();
                Assert.AreEqual(2, provider.ReleaseCalls);
                Assert.AreEqual(2, provider.LoadCalls);
                lease.Dispose();
                Assert.AreEqual(3, provider.ReleaseCalls);
                Assert.IsTrue(service.Validate(out string error), error);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void ConfigDispose_RetriesFailedProviderRelease()
        {
            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            var provider = new FakeProvider { Load = key => UniTask.FromResult(new ProviderHandle(key, asset)) };
            provider.OnRelease = _ =>
            {
                if (provider.ReleaseCalls == 1) throw new InvalidOperationException("release failed");
            };
            var service = new AssetService(provider);
            var configs = new ConfigService(service);
            try
            {
                configs.LoadAsync<FakeAsset>("config").GetAwaiter().GetResult();
                Assert.Throws<InvalidOperationException>(() => configs.DisposeAsync().GetAwaiter().GetResult());
                Assert.AreEqual(1, service.ActiveLeases);
                Assert.Throws<ObjectDisposedException>(() => configs.LoadAsync<FakeAsset>("config").GetAwaiter().GetResult());

                configs.DisposeAsync().GetAwaiter().GetResult();
                Assert.AreEqual(2, provider.ReleaseCalls);
                Assert.AreEqual(0, service.ActiveLeases);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void AddressAndGuid_ShareOneLoad()
        {
            var provider = new FakeProvider { Canonical = _ => "canonical:enemy" };   // 两种键规范化到同一资源
            var service = new AssetService(provider);

            AssetLease<FakeAsset> a = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();
            AssetLease<FakeAsset> b = service.AcquireAsync<FakeAsset>("guid:1234", default).GetAwaiter().GetResult();

            Assert.AreEqual(1, provider.LoadCalls, "地址与 GUID 指向同一资源，只能加载一次");
            Assert.AreEqual(2, service.ActiveLeases);

            a.Dispose();
            b.Dispose();
        }

        [Test]
        public void LoadFailure_ProducesNoLease_AndCanRetry()
        {
            var provider = new FakeProvider();
            int attempt = 0;
            provider.Load = _ =>
            {
                attempt++;
                if (attempt == 1) throw new InvalidOperationException("load failed");
                return UniTask.FromResult(new ProviderHandle("enemy", ScriptableObject.CreateInstance<FakeAsset>()));
            };

            var service = new AssetService(provider);

            var awaiter = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter();
            Assert.Throws<InvalidOperationException>(() => awaiter.GetResult());
            Assert.AreEqual(0, service.ActiveLeases, "失败不应产生租约");
            Assert.AreEqual(0, service.PendingRequests);

            // 第二次应重试（条目已清理）
            AssetLease<FakeAsset> lease = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();
            Assert.IsNotNull(lease);
            Assert.AreEqual(2, provider.LoadCalls);
            lease.Dispose();
        }

        [Test]
        public void PreCancelledToken_FailsWithoutLoading()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var awaiter = service.AcquireAsync<FakeAsset>("enemy", cts.Token).GetAwaiter();
            Assert.Throws<OperationCanceledException>(() => awaiter.GetResult());
            Assert.AreEqual(0, provider.LoadCalls, "已取消的请求不应触发加载");
        }

        [Test]
        public void CacheAndBusiness_Mixed_OneLoadTwoLeases()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);

            AssetLease<FakeAsset> business = service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();
            AssetLease<FakeAsset> cache = service.AcquireCacheAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();

            Assert.AreEqual(1, provider.LoadCalls);
            Assert.AreEqual(AssetLeaseKind.Business, business.Kind);
            Assert.AreEqual(AssetLeaseKind.Cache, cache.Kind);

            business.Dispose();
            Assert.AreEqual(0, provider.ReleaseCalls, "缓存租约仍在，底层不能释放");
            cache.Dispose();
            Assert.AreEqual(1, provider.ReleaseCalls);
        }

        [Test]
        public void AllWaitersCancelled_LoadCompletes_ReleasesHandle()
        {
            var pending = new UniTaskCompletionSource<ProviderHandle>();
            var provider = new FakeProvider { Load = _ => pending.Task };
            var service = new AssetService(provider);
            var cts = new CancellationTokenSource();

            var awaiter = service.AcquireAsync<FakeAsset>("enemy", cts.Token).GetAwaiter();
            Assert.AreEqual(1, service.PendingRequests);

            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => awaiter.GetResult());

            // 加载最终完成，但已经没人要了
            pending.TrySetResult(new ProviderHandle("enemy", ScriptableObject.CreateInstance<FakeAsset>()));

            Assert.AreEqual(1, provider.ReleaseCalls, "无人等待时加载完成必须立即归还句柄");
            Assert.AreEqual(0, service.ActiveLeases);
            Assert.AreEqual(0, service.PendingRequests);
        }

        [Test]
        public void CountingInvariants_HoldAfterMixedOperations()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);

            AssetLease<FakeAsset> a = service.AcquireAsync<FakeAsset>("a", default).GetAwaiter().GetResult();
            AssetLease<FakeAsset> b = service.AcquireAsync<FakeAsset>("a", default).GetAwaiter().GetResult();
            AssetLease<FakeAsset> cache = service.AcquireCacheAsync<FakeAsset>("a", default).GetAwaiter().GetResult();

            Assert.AreEqual(3, service.ActiveLeases);
            Assert.IsTrue(service.Validate(out string error0), error0);

            a.Dispose();
            b.Dispose();
            cache.Dispose();

            Assert.AreEqual(0, service.ActiveLeases);
            Assert.AreEqual(1, provider.ReleaseCalls);
            Assert.IsTrue(service.Validate(out string error1), error1);
        }

        [Test]
        public void CountingInvariants_HoldAfterFailureAndCancel()
        {
            var provider = new FakeProvider();
            int attempt = 0;
            provider.Load = _ =>
            {
                attempt++;
                if (attempt == 1) throw new InvalidOperationException("boom");
                return UniTask.FromResult(new ProviderHandle("k", ScriptableObject.CreateInstance<FakeAsset>()));
            };

            var service = new AssetService(provider);

            Assert.Throws<InvalidOperationException>(() => service.AcquireAsync<FakeAsset>("k", default).GetAwaiter().GetResult());

            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => service.AcquireAsync<FakeAsset>("k", cts.Token).GetAwaiter().GetResult());

            AssetLease<FakeAsset> lease = service.AcquireAsync<FakeAsset>("k", default).GetAwaiter().GetResult();
            Assert.AreEqual(1, service.ActiveLeases);

            lease.Dispose();

            Assert.AreEqual(0, service.ActiveLeases);
            Assert.IsTrue(service.Validate(out string error), error);
        }

        [Test]
        public void PendingRequests_ReturnsToZero()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);

            service.AcquireAsync<FakeAsset>("enemy", default).GetAwaiter().GetResult();

            Assert.AreEqual(0, service.PendingRequests);
        }
    }
}
