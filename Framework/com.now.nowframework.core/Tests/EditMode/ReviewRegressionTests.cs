using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core.Events;
using NowFrameWork.Core.Pooling;

namespace NowFrameWork.Core.Tests
{
    public class ReviewRegressionTests
    {
        sealed class Item : IPoolable
        {
            public bool FailSpawn;
            public bool FailDespawn;
            public void OnSpawn() { if (FailSpawn) throw new InvalidOperationException("spawn"); }
            public void OnDespawn() { if (FailDespawn) throw new InvalidOperationException("despawn"); }
        }
        sealed class AsyncFactory : IAsyncPoolItemFactory<Item>
        {
            public readonly UniTaskCompletionSource<Item> Gate = new UniTaskCompletionSource<Item>();
            public int Destroyed;
            public UniTask<Item> CreateAsync(CancellationToken ct) => Gate.Task;
            public void Destroy(Item item) => Destroyed++;
        }
        sealed class DelayedModule : IAppModule
        {
            public readonly UniTaskCompletionSource Gate = new UniTaskCompletionSource();
            public int Starts;
            public int Stops;
            public string Id => "delayed";
            public IReadOnlyList<string> DependsOn => Array.Empty<string>();
            public UniTask InitializeAsync(CancellationToken ct) => Gate.Task;
            public UniTask StartAsync(CancellationToken ct) { Starts++; return UniTask.CompletedTask; }
            public UniTask ShutdownAsync(CancellationToken ct) { Stops++; return UniTask.CompletedTask; }
        }
        struct Evt { public int Depth; }

        [Test]
        public void RentAsync_DisposedWhileCreating_DestroysLateItem()
        {
            var factory = new AsyncFactory();
            var pool = new ObjectPool<Item>(() => new Item(), asyncFactory: factory);
            var pending = pool.RentAsync(default).AsTask();
            pool.Dispose(true);
            factory.Gate.TrySetResult(new Item());
            Assert.Throws<PoolUsageException>(() => pending.GetAwaiter().GetResult());
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(1, factory.Destroyed);
            Assert.AreEqual(pool.Stats.Created, pool.Stats.Destroyed);
        }

        [Test]
        public void Get_And_Prewarm_RespectAsyncReservations()
        {
            var factory = new AsyncFactory();
            var pool = new ObjectPool<Item>(() => new Item(), maxSize: 1, asyncFactory: factory);
            var pending = pool.RentAsync(default).AsTask();
            Assert.Throws<PoolExhaustedException>(() => pool.Get());
            Assert.AreEqual(0, pool.Prewarm(1));
            factory.Gate.TrySetResult(new Item());
            pool.Release(pending.GetAwaiter().GetResult());
            pool.Dispose();
        }

        [Test]
        public void AsyncItems_AreDestroyedByCreatingFactory()
        {
            var factory = new AsyncFactory();
            int syncDestroyed = 0;
            var pool = new ObjectPool<Item>(() => new Item(), _ => syncDestroyed++, asyncFactory: factory);
            factory.Gate.TrySetResult(new Item());
            pool.RentAsync(default).GetAwaiter().GetResult();
            pool.Dispose(true);
            Assert.AreEqual(1, factory.Destroyed);
            Assert.AreEqual(0, syncDestroyed);
        }

        [Test]
        public void SpawnFailure_DestroysUnreturnedItem()
        {
            var pool = new ObjectPool<Item>(() => new Item { FailSpawn = true });
            Assert.Throws<InvalidOperationException>(() => pool.Get());
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(0, pool.TotalCount);
            Assert.AreEqual(pool.Stats.Created, pool.Stats.Destroyed);
            pool.Dispose();
        }

        [Test]
        public void DespawnFailure_DestroysRemovedItem()
        {
            var pool = new ObjectPool<Item>(() => new Item { FailDespawn = true });
            Item item = pool.Get();
            Assert.Throws<InvalidOperationException>(() => pool.Release(item));
            Assert.AreEqual(0, pool.TotalCount);
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(0, pool.InactiveCount);
            pool.Dispose();
        }

        [Test]
        public void RentAsync_PreCancelled_DoesNotTakeIdleItem()
        {
            var pool = new ObjectPool<Item>(() => new Item(), prewarm: 1);
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => pool.RentAsync(cts.Token).GetAwaiter().GetResult());
            Assert.AreEqual(1, pool.InactiveCount);
            pool.Dispose();
            cts.Dispose();
        }

        [Test]
        public void NestedPublish_DoesNotRewriteOuterSnapshot()
        {
            var bus = new EventBus();
            var calls = new List<string>();
            IEventSubscription old = null;
            void C(in Evt e) => calls.Add("C" + e.Depth);
            void B(in Evt e) => calls.Add("B" + e.Depth);
            void A(in Evt e)
            {
                calls.Add("A" + e.Depth);
                if (e.Depth != 0) return;
                old.Dispose();
                bus.Subscribe<Evt>(C);
                bus.Publish(new Evt { Depth = 1 });
            }
            bus.Subscribe<Evt>(A);
            old = bus.Subscribe<Evt>(B);
            bus.Publish(new Evt { Depth = 0 });
            CollectionAssert.AreEqual(new[] { "A0", "A1", "C1", "B0" }, calls);
        }

        [Test]
        public void Shutdown_DuringStartup_WaitsAndPreventsStart()
        {
            var hub = new ModuleHub();
            var module = new DelayedModule();
            hub.Register(module);
            var startup = hub.StartupAsync().AsTask();
            var shutdown = hub.ShutdownAsync().AsTask();
            var shutdownAgain = hub.ShutdownAsync().AsTask();
            Assert.IsFalse(shutdown.IsCompleted);
            Assert.IsFalse(shutdownAgain.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => hub.StartupAsync().GetAwaiter().GetResult());
            module.Gate.TrySetResult();
            Assert.Catch<OperationCanceledException>(() => startup.GetAwaiter().GetResult());
            shutdown.GetAwaiter().GetResult();
            shutdownAgain.GetAwaiter().GetResult();
            Assert.AreEqual(0, module.Starts);
            Assert.AreEqual(1, module.Stops);
        }
    }
}
