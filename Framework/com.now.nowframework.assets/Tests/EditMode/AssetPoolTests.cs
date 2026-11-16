using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core.Time;
using UnityEngine;

namespace NowFrameWork.Assets.Tests
{
    /// <summary>P-01 ~ P-05：资源池的保活租约与销毁时序。</summary>
    public class AssetPoolTests
    {
        sealed class FakeAsset : ScriptableObject
        {
        }

        sealed class FakeProvider : IAssetProvider
        {
            public int LoadCalls;
            public int ReleaseCalls;

            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
                => UniTask.FromResult(key.Address);

            public UniTask<ProviderHandle> LoadAsync(string canonicalKey, CancellationToken ct)
            {
                LoadCalls++;
                return UniTask.FromResult(new ProviderHandle(canonicalKey, ScriptableObject.CreateInstance<FakeAsset>()));
            }

            public void Release(ProviderHandle handle) => ReleaseCalls++;
        }

        static (AssetPool<FakeAsset> pool, FakeProvider provider, AssetService service) CreatePool()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);
            var clock = new ManualClock();

            AssetPool<FakeAsset> pool = AssetPool<FakeAsset>.CreateAsync(
                service,
                AssetKey.From("enemy"),
                () => ScriptableObject.CreateInstance<FakeAsset>(),
                UnityEngine.Object.DestroyImmediate,
                clock,
                maxSize: 4,
                settleFrames: 8,
                frameWaiter: () => UniTask.CompletedTask).GetAwaiter().GetResult();

            return (pool, provider, service);
        }

        [Test]
        public void CreateAsync_AcquiresKeepAlive_AndRents()
        {
            var (pool, provider, service) = CreatePool();

            Assert.AreEqual(1, provider.LoadCalls, "建池时申请一次保活租约");
            Assert.AreEqual(1, service.ActiveLeases);

            FakeAsset item = pool.Rent();
            Assert.IsNotNull(item);
            Assert.AreEqual(1, pool.ActiveCount);

            pool.Release(item);
            pool.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void Rent_ReusesIdleInstance()
        {
            var (pool, _, _) = CreatePool();

            FakeAsset first = pool.Rent();
            pool.Release(first);
            FakeAsset second = pool.Rent();

            Assert.AreSame(first, second, "归还后应复用同一实例");

            pool.Release(second);
            pool.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void DisposeAsync_DestroysInstances_ThenReleasesKeepAlive()
        {
            var (pool, provider, service) = CreatePool();

            FakeAsset a = pool.Rent();
            FakeAsset b = pool.Rent();
            pool.Release(b);

            pool.DisposeAsync().GetAwaiter().GetResult();

            Assert.IsTrue(a == null, "活跃实例必须被销毁");
            Assert.AreEqual(1, provider.ReleaseCalls, "保活租约必须归还");
            Assert.AreEqual(0, service.ActiveLeases);
        }

        [Test]
        public void DisposeAsync_TimeoutRetainsLease_AndCanRetryAfterDestruction()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);

            // 销毁回调故意不销毁：模拟"实例迟迟不消失"
            AssetPool<FakeAsset> pool = AssetPool<FakeAsset>.CreateAsync(
                service,
                AssetKey.From("stuck"),
                () => ScriptableObject.CreateInstance<FakeAsset>(),
                _ => { },
                new ManualClock(),
                settleFrames: 4,
                frameWaiter: () => UniTask.CompletedTask).GetAwaiter().GetResult();

            FakeAsset instance = pool.Rent();

            Assert.Throws<TimeoutException>(() => pool.DisposeAsync().GetAwaiter().GetResult());
            Assert.AreEqual(0, provider.ReleaseCalls, "实例存活时不能卸载其依赖");
            Assert.AreEqual(1, service.ActiveLeases);
            UnityEngine.Object.DestroyImmediate(instance);
            pool.DisposeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, provider.ReleaseCalls);
            Assert.AreEqual(0, service.ActiveLeases);
        }

        [Test]
        public void DisposeAsync_IsIdempotent()
        {
            var (pool, provider, _) = CreatePool();

            pool.Rent();
            pool.DisposeAsync().GetAwaiter().GetResult();
            pool.DisposeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, provider.ReleaseCalls, "重复释放不应重复归还租约");
        }
    }
}
