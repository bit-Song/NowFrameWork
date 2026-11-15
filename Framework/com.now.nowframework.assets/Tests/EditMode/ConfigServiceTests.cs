using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace NowFrameWork.Assets.Tests
{
    /// <summary>配置服务：常驻缓存、类型校验、租约归还。</summary>
    public class ConfigServiceTests
    {
        sealed class StatConfig : ScriptableObject
        {
        }

        sealed class OtherConfig : ScriptableObject
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

                ScriptableObject asset = canonicalKey == "other"
                    ? ScriptableObject.CreateInstance<OtherConfig>()
                    : ScriptableObject.CreateInstance<StatConfig>();

                return UniTask.FromResult(new ProviderHandle(canonicalKey, asset));
            }

            public void Release(ProviderHandle handle) => ReleaseCalls++;
        }

        static (ConfigService config, FakeProvider provider, AssetService service) Create()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);
            return (new ConfigService(service), provider, service);
        }

        [Test]
        public void Load_SameKey_LoadsOnce()
        {
            var (config, provider, service) = Create();

            StatConfig first = config.LoadAsync<StatConfig>("stat").GetAwaiter().GetResult();
            StatConfig second = config.LoadAsync<StatConfig>("stat").GetAwaiter().GetResult();

            Assert.AreSame(first, second, "同一 key 应返回同一实例");
            Assert.AreEqual(1, provider.LoadCalls, "只应加载一次");
            Assert.AreEqual(1, config.Count);
            Assert.AreEqual(1, service.ActiveLeases);

            config.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void DisposeAsync_ReleasesAllLeases()
        {
            var (config, provider, service) = Create();

            config.LoadAsync<StatConfig>("stat").GetAwaiter().GetResult();
            config.LoadAsync<OtherConfig>("other").GetAwaiter().GetResult();

            Assert.AreEqual(2, service.ActiveLeases);

            config.DisposeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0, service.ActiveLeases, "配置释放后不应残留租约");
            Assert.AreEqual(2, provider.ReleaseCalls);

            // 幂等
            config.DisposeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(2, provider.ReleaseCalls);
        }

        [Test]
        public void Load_TypeMismatch_Throws_AndReleasesLease()
        {
            var (config, provider, service) = Create();

            Assert.Throws<InvalidCastException>(() =>
                config.LoadAsync<StatConfig>("other").GetAwaiter().GetResult());

            Assert.AreEqual(0, service.ActiveLeases, "类型不匹配时必须归还刚拿到的租约");
            Assert.AreEqual(1, provider.ReleaseCalls);
            Assert.AreEqual(0, config.Count);
        }
    }
}