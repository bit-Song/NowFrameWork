using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace NowFrameWork.Core.Tests
{
    /// <summary>
    /// C06 对象池用例。重点验证「不静默」：重复归还、超额、自动回收都必须是显式行为。
    /// </summary>
    public class PoolTests
    {
        sealed class Item : IPoolable
        {
            public int Spawns;
            public int Despawns;

            public void OnSpawn() => Spawns++;
            public void OnDespawn() => Despawns++;
        }

        sealed class Counter
        {
            public int Created;
            public int Destroyed;
        }

        static ObjectPool<Item> CreatePool(Counter counter, int maxSize = 0, double autoRecycle = 0d,
            int prewarm = 0, IClock clock = null, System.Func<Item, bool> isValid = null)
        {
            return new ObjectPool<Item>(
                () => { counter.Created++; return new Item(); },
                _ => counter.Destroyed++,
                maxSize, autoRecycle, prewarm, isValid, clock);
        }

        [Test]
        public void Get_WhenEmpty_CreatesNew()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);

            Item item = pool.Get();

            Assert.IsNotNull(item);
            Assert.AreEqual(1, counter.Created);
            Assert.AreEqual(1, pool.ActiveCount);
            Assert.AreEqual(0, pool.InactiveCount);
        }

        [Test]
        public void Release_ThenGet_ReusesSameInstance()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);

            Item first = pool.Get();
            pool.Release(first);
            Item second = pool.Get();

            Assert.AreSame(first, second, "归还后应复用同一实例");
            Assert.AreEqual(1, counter.Created, "不应重复创建");
        }

        [Test]
        public void Release_Twice_Throws()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);

            Item item = pool.Get();
            pool.Release(item);

            Assert.Throws<PoolUsageException>(() => pool.Release(item), "重复归还必须报错");
        }

        [Test]
        public void Release_ForeignObject_Throws()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);

            Assert.Throws<PoolUsageException>(() => pool.Release(new Item()), "归还不属于本池的对象必须报错");
        }

        [Test]
        public void Get_AtMaxSize_ThrowsExhausted()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter, maxSize: 2);

            pool.Get();
            pool.Get();

            Assert.Throws<PoolExhaustedException>(() => pool.Get(), "达到上限必须显式失败，不能静默返回 null");
        }

        [Test]
        public void TryGet_WhenEmpty_DoesNotCreate()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);

            Assert.IsFalse(pool.TryGet(out Item item));
            Assert.IsNull(item);
            Assert.AreEqual(0, counter.Created, "TryGet 不应创建对象");
        }

        [Test]
        public void Prewarm_CreatesIdle_AndRespectsMaxSize()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter, maxSize: 3);

            int made = pool.Prewarm(5);

            Assert.AreEqual(3, made);
            Assert.AreEqual(3, pool.InactiveCount);
            Assert.AreEqual(3, counter.Created);
        }

        [Test]
        public void Tick_RecyclesIdleAfterTimeout()
        {
            var clock = new ManualClock();
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter, autoRecycle: 1.0, clock: clock);

            Item item = pool.Get();
            pool.Release(item);
            Assert.AreEqual(1, pool.InactiveCount);

            clock.Advance(0.5f);
            pool.Tick(clock.UnscaledNow);
            Assert.AreEqual(1, pool.InactiveCount, "未超时不应回收");

            clock.Advance(1.0f);
            pool.Tick(clock.UnscaledNow);
            Assert.AreEqual(0, pool.InactiveCount, "超时应回收");
            Assert.AreEqual(1, counter.Destroyed);
        }

        [Test]
        public void Tick_NeverRecyclesActiveObjects()
        {
            var clock = new ManualClock();
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter, autoRecycle: 1.0, clock: clock);

            pool.Get();   // 保持活跃

            clock.Advance(10f);
            pool.Tick(clock.UnscaledNow);

            Assert.AreEqual(1, pool.ActiveCount, "活跃对象永远不能被自动回收");
            Assert.AreEqual(0, counter.Destroyed);
        }

        [Test]
        public void Poolable_ReceivesSpawnAndDespawn()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);

            Item item = pool.Get();
            Assert.AreEqual(1, item.Spawns);

            pool.Release(item);
            Assert.AreEqual(1, item.Despawns);
        }

        [Test]
        public void Clear_WithoutActive_KeepsActiveObjects()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);

            pool.Get();                    // 活跃
            Item idle = pool.Get();
            pool.Release(idle);            // 闲置

            pool.Clear(includeActive: false);

            Assert.AreEqual(1, counter.Destroyed, "只销毁闲置对象");
            Assert.AreEqual(1, pool.ActiveCount, "活跃对象保留");
        }

        [Test]
        public void Get_SkipsInvalidObjects()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter, isValid: _ => false);

            Item idle = pool.Get();
            pool.Release(idle);

            Item fresh = pool.Get();   // 旧对象失效 → 销毁并新建

            Assert.AreNotSame(idle, fresh);
            Assert.AreEqual(1, counter.Destroyed);
            Assert.AreEqual(2, counter.Created);
        }

        [Test]
        public void PoolService_SameKey_ReturnsSamePool()
        {
            var clock = new ManualClock();
            var service = new PoolService(clock);

            ObjectPool<Item> a = service.GetOrCreate("item", () => new Item());
            ObjectPool<Item> b = service.GetOrCreate("item", () => new Item());

            Assert.AreSame(a, b);
            Assert.AreEqual(1, service.PoolCount);
        }

        [Test]
        public void PoolService_Tick_DrivesAllPools()
        {
            var clock = new ManualClock();
            var service = new PoolService(clock);
            var counterA = new Counter();
            var counterB = new Counter();

            ObjectPool<Item> a = service.GetOrCreate("a", () => { counterA.Created++; return new Item(); },
                _ => counterA.Destroyed++, autoRecycleSeconds: 1.0);
            ObjectPool<Item> b = service.GetOrCreate("b", () => { counterB.Created++; return new Item(); },
                _ => counterB.Destroyed++, autoRecycleSeconds: 1.0);

            a.Release(a.Get());
            b.Release(b.Get());

            clock.Advance(2f);
            service.Tick();

            Assert.AreEqual(1, counterA.Destroyed);
            Assert.AreEqual(1, counterB.Destroyed);
            Assert.AreEqual(0, a.InactiveCount);
            Assert.AreEqual(0, b.InactiveCount);
        }

        // ---- 释放语义与对账（资源卸载路径）----

        sealed class TestFactory : IPoolItemFactory<Item>
        {
            public readonly Counter Counter = new Counter();

            public Item Create()
            {
                Counter.Created++;
                return new Item();
            }

            public void Destroy(Item item) => Counter.Destroyed++;
        }

        [Test]
        public void Factory_IsUsedForCreateAndDestroy()
        {
            var factory = new TestFactory();
            var pool = new ObjectPool<Item>(factory);

            Item item = pool.Get();
            pool.Release(item);
            pool.Dispose();

            Assert.AreEqual(1, factory.Counter.Created);
            Assert.AreEqual(1, factory.Counter.Destroyed);
        }

        [Test]
        public void Dispose_WithActiveObjects_Throws()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);
            pool.Get();

            Assert.Throws<InvalidOperationException>(() => pool.Dispose(),
                "仍有未归还对象时必须拒绝释放，否则会出现悬空引用");
            Assert.IsFalse(pool.IsDisposed);
        }

        [Test]
        public void Dispose_Force_DestroysEverything()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);

            pool.Get();                          // 活跃
            Item idle = pool.Get();
            pool.Release(idle);                  // 闲置

            pool.Dispose(force: true);

            Assert.IsTrue(pool.IsDisposed);
            Assert.AreEqual(2, counter.Destroyed);
            Assert.AreEqual(0, pool.TotalCount);
        }

        [Test]
        public void Release_AfterDispose_Throws()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter);
            Item item = pool.Get();

            pool.Dispose(force: true);

            Assert.Throws<PoolUsageException>(() => pool.Release(item));
            Assert.Throws<PoolUsageException>(() => pool.Get());
        }

        [Test]
        public void Stats_CreatedEqualsDestroyed_AfterDispose()
        {
            var counter = new Counter();
            ObjectPool<Item> pool = CreatePool(counter, prewarm: 2);

            Item a = pool.Get();
            Item b = pool.Get();
            pool.Release(a);
            pool.Dispose(force: true);

            PoolStats stats = pool.Stats;
            Assert.AreEqual(stats.Created, stats.Destroyed, "对账：创建数必须等于销毁数，否则说明有租约泄漏");
            Assert.AreEqual(0, stats.Active);
            Assert.AreEqual(0, stats.Inactive);
        }

        [Test]
        public void PoolService_DisposePool_RemovesAndReleases()
        {
            var clock = new ManualClock();
            var service = new PoolService(clock);
            var counter = new Counter();

            ObjectPool<Item> pool = service.GetOrCreate("res:enemy", () => { counter.Created++; return new Item(); },
                _ => counter.Destroyed++);
            pool.Get();

            Assert.IsTrue(service.DisposePool("res:enemy", force: true));
            Assert.AreEqual(0, service.PoolCount);
            Assert.AreEqual(1, counter.Destroyed, "卸载资源时池对象必须被销毁（进而释放租约）");
            Assert.IsFalse(service.DisposePool("res:enemy"), "重复卸载应安全返回 false");
        }

        [Test]
        public void PoolService_Dispose_ReleasesAllPools()
        {
            var clock = new ManualClock();
            var service = new PoolService(clock);
            var counter = new Counter();

            service.GetOrCreate("a", () => { counter.Created++; return new Item(); }, _ => counter.Destroyed++);
            service.GetOrCreate("b", () => { counter.Created++; return new Item(); }, _ => counter.Destroyed++);

            service.Dispose();

            Assert.AreEqual(0, service.PoolCount);
            Assert.AreEqual(2, counter.Destroyed);
        }
    }
}