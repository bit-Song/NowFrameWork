using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core.Threading;
using NowFrameWork.Core.Time;
using UnityEngine;

namespace NowFrameWork.Assets.Tests
{
    public class MainThreadGuardTests
    {
        sealed class TestAsset : ScriptableObject { }
        sealed class Provider : IAssetProvider
        {
            public int ResolveCalls;
            public int ReleaseCalls;
            public TestAsset Asset;
            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
            { ResolveCalls++; return UniTask.FromResult(key.Address); }
            public UniTask<ProviderHandle> LoadAsync(string key, CancellationToken ct)
                => UniTask.FromResult(new ProviderHandle(key, Asset));
            public void Release(ProviderHandle handle) => ReleaseCalls++;
        }
        TestAsset _asset;
        Provider _provider;
        AssetService _service;
        [SetUp] public void Setup()
        {
            _asset = ScriptableObject.CreateInstance<TestAsset>();
            _provider = new Provider { Asset = _asset };
            _service = new AssetService(_provider);
        }
        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(_asset);

        static void RejectedOnWorker(Action action)
        {
            Exception error = null;
            var worker = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
            worker.IsBackground = true;
            worker.Start();
            Assert.IsTrue(worker.Join(3000), "断言须在 Provider 或 Unity API 执行前同步完成");
            Assert.That(error, Is.TypeOf<InvalidOperationException>(), error?.ToString());
        }

        [Test]
        public void WorkerAcquire_IsRejectedBeforeProviderOrCounters()
        {
            RejectedOnWorker(() => _service.AcquireAsync<TestAsset>("asset", default).GetAwaiter().GetResult());
            Assert.AreEqual(0, _provider.ResolveCalls);
            Assert.AreEqual(0, _service.ActiveLeases);
            Assert.AreEqual(0, _service.PendingRequests);
        }

        [Test]
        public void WorkerLeaseDispose_DoesNotConsumeLease()
        {
            var lease = _service.AcquireAsync<TestAsset>("asset", default).GetAwaiter().GetResult();
            RejectedOnWorker(lease.Dispose);
            Assert.IsFalse(lease.IsReleased);
            Assert.AreEqual(1, _service.ActiveLeases);
            Assert.AreEqual(0, _provider.ReleaseCalls);
            lease.Dispose();
            Assert.AreEqual(0, _service.ActiveLeases);
            Assert.AreEqual(1, _provider.ReleaseCalls);
        }

        [Test]
        public void WorkerLeaseAssetAccess_IsRejectedBeforeUnityObjectUse()
        {
            var lease = _service.AcquireAsync<TestAsset>("asset", default).GetAwaiter().GetResult();
            RejectedOnWorker(() => { var ignored = lease.Asset; });
            Assert.AreSame(_asset, lease.Asset);
            lease.Dispose();
        }

        [Test]
        public void WorkerConfigLoad_DoesNotReachProvider()
        {
            var config = new ConfigService(_service);
            RejectedOnWorker(() => config.LoadAsync<TestAsset>("asset").GetAwaiter().GetResult());
            Assert.AreEqual(0, config.Count);
            Assert.AreEqual(0, _provider.ResolveCalls);
            config.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void WorkerScopeClose_DoesNotChangeState()
        {
            var scope = new Scope("thread-probe", null, new ManualClock(), frameWaiter: () => UniTask.CompletedTask);
            scope.StartupAsync().GetAwaiter().GetResult();
            RejectedOnWorker(() => scope.DisposeAsync().GetAwaiter().GetResult());
            Assert.AreEqual(ScopeState.Running, scope.State);
            scope.DisposeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ScopeState.Stopped, scope.State);
        }

        [Test]
        public void WorkerSceneUnload_DoesNotStartBackend()
        {
            int calls = 0;
            var lease = new SceneLease("scene", () => { calls++; return UniTask.CompletedTask; });
            RejectedOnWorker(() => lease.UnloadAsync().GetAwaiter().GetResult());
            Assert.AreEqual(0, calls);
            Assert.IsFalse(lease.IsUnloaded);
            lease.UnloadAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, calls);
        }
    }
}
