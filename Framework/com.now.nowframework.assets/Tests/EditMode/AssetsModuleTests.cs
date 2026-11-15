using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace NowFrameWork.Assets.Tests
{
    /// <summary>资源模块与框架生命周期的衔接。</summary>
    public class AssetsModuleTests
    {
        sealed class FakeAsset : ScriptableObject
        {
        }

        sealed class FakeProvider : IAssetProvider
        {
            public int LoadCalls;

            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
                => UniTask.FromResult(key.Address);

            public UniTask<ProviderHandle> LoadAsync(string canonicalKey, CancellationToken ct)
            {
                LoadCalls++;
                return UniTask.FromResult(new ProviderHandle(canonicalKey, ScriptableObject.CreateInstance<FakeAsset>()));
            }

            public void Release(ProviderHandle handle) { }
        }

        [Test]
        public void Initialize_DelegatesToService()
        {
            var service = new AssetService(new FakeProvider());
            var module = new AssetsModule(service);

            Assert.AreEqual("assets", module.Id);
            module.InitializeAsync(default).GetAwaiter().GetResult();
            Assert.AreEqual(0, module.DependsOn.Count);
        }

        [Test]
        public void Shutdown_WithLease_DoesNotThrow()
        {
            var provider = new FakeProvider();
            var service = new AssetService(provider);
            var module = new AssetsModule(service);

            AssetLease<FakeAsset> lease = service.AcquireAsync<FakeAsset>("x", default).GetAwaiter().GetResult();

            // 有租约时关闭：只记 Error，不抛、不擅自释放
            Assert.DoesNotThrow(() => module.ShutdownAsync(default).GetAwaiter().GetResult());
            Assert.AreEqual(1, service.ActiveLeases, "模块不能替业务释放租约");

            lease.Dispose();
        }
    }
}