using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Core.Platform;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Input.Tests
{
    /// <summary>返回导航分发的规则：一帧一次、UI 优先、后台暂停、异常不抛出。</summary>
    public class InputModuleTests
    {
        sealed class FakeSource : IBackSource
        {
            public bool Pending;
            public int Polls;

            public bool TryConsumeBack()
            {
                Polls++;
                if (!Pending) return false;

                Pending = false;   // 消费掉，模拟真实按键只上报一次
                return true;
            }
        }

        sealed class FakeFallback : IBackFallback
        {
            public int Calls;
            public bool Handles = true;

            public bool OnBack()
            {
                Calls++;
                return Handles;
            }
        }

        [SetUp]
        public void SetUp() => MainThreadGuard.BindCurrentThread();

        static InputModule Create(Func<CancellationToken, UniTask<bool>> ui, IBackFallback fallback = null,
            IAppLifecycle lifecycle = null, params IBackSource[] sources)
        {
            var module = new InputModule(ui, fallback, lifecycle, log: null, sources);
            module.InitializeAsync(default).GetAwaiter().GetResult();
            module.StartAsync(default).GetAwaiter().GetResult();
            return module;
        }

        static UniTask<bool> Handled() => UniTask.FromResult(true);
        static UniTask<bool> Ignored() => UniTask.FromResult(false);

        [Test]
        public void Tick_WithPress_DispatchesToUi()
        {
            int uiCalls = 0;
            var source = new FakeSource { Pending = true };

            InputModule module = Create(_ => { uiCalls++; return Handled(); }, sources: new IBackSource[] { source });

            module.Tick(0.016f, 0.016f);

            Assert.AreEqual(1, uiCalls);
            Assert.AreEqual(BackRoute.Ui, module.LastRoute);
            Assert.AreEqual(1, module.BackCount);
        }

        [Test]
        public void Tick_WhenUiHandles_DoesNotCallFallback()
        {
            var fallback = new FakeFallback();
            InputModule module = Create(_ => Handled(), fallback,
                sources: new IBackSource[] { new FakeSource { Pending = true } });

            module.Tick(0.016f, 0.016f);

            Assert.AreEqual(0, fallback.Calls, "UI 消费后不应再走兜底");
            Assert.AreEqual(BackRoute.Ui, module.LastRoute);
        }

        [Test]
        public void Tick_WhenUiIgnores_CallsFallbackOnce()
        {
            var fallback = new FakeFallback();
            InputModule module = Create(_ => Ignored(), fallback,
                sources: new IBackSource[] { new FakeSource { Pending = true } });

            module.Tick(0.016f, 0.016f);

            Assert.AreEqual(1, fallback.Calls);
            Assert.AreEqual(BackRoute.Fallback, module.LastRoute);
        }

        [Test]
        public void Tick_WithTwoSources_DispatchesOnce()
        {
            int uiCalls = 0;
            var a = new FakeSource { Pending = true };
            var b = new FakeSource { Pending = true };

            InputModule module = Create(_ => { uiCalls++; return Handled(); },
                sources: new IBackSource[] { a, b });

            module.Tick(0.016f, 0.016f);

            Assert.AreEqual(1, uiCalls, "一帧只应分发一次");
            Assert.IsTrue(b.Pending, "第一个来源消费后应直接跳出，不再询问后续来源");
        }

        [Test]
        public void Tick_WhileDispatching_IgnoresNewPress()
        {
            int uiCalls = 0;
            var gate = new UniTaskCompletionSource<bool>();
            var source = new FakeSource { Pending = true };

            InputModule module = Create(_ => { uiCalls++; return gate.Task; },
                sources: new IBackSource[] { source });

            module.Tick(0.016f, 0.016f);   // 第一次：进入分发并挂起
            Assert.AreEqual(1, uiCalls);

            source.Pending = true;
            module.Tick(0.016f, 0.016f);   // 过渡期间：必须忽略
            Assert.AreEqual(1, uiCalls, "分发未完成时不应连发");

            gate.TrySetResult(true);
            Assert.AreEqual(BackRoute.Ui, module.LastRoute);

            source.Pending = true;
            module.Tick(0.016f, 0.016f);   // 完成后恢复接收
            Assert.AreEqual(2, uiCalls);
        }

        [Test]
        public void Tick_WhenPaused_DoesNotDispatch_AndDoesNotCatchUp()
        {
            int uiCalls = 0;
            var life = new AppLifecycle();
            life.NotifyResume();

            var source = new FakeSource { Pending = true };
            InputModule module = Create(_ => { uiCalls++; return Handled(); }, null, life,
                new IBackSource[] { source });

            life.NotifyPause();
            module.Tick(0.016f, 0.016f);   // 后台：不处理
            Assert.AreEqual(0, uiCalls);
            Assert.IsTrue(source.Pending, "后台期间不应消费按键");

            life.NotifyResume();
            module.Tick(0.016f, 0.016f);   // 恢复后才处理
            Assert.AreEqual(1, uiCalls);
        }

        [Test]
        public void Tick_AfterShutdown_DoesNotDispatch()
        {
            int uiCalls = 0;
            InputModule module = Create(_ => { uiCalls++; return Handled(); },
                sources: new IBackSource[] { new FakeSource { Pending = true } });

            module.ShutdownAsync(default).GetAwaiter().GetResult();
            module.Tick(0.016f, 0.016f);

            Assert.AreEqual(0, uiCalls);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingBack_DoesNotRunFallbackAfterBackground(bool resumeBeforeCompletion)
        {
            var life = new AppLifecycle();
            var pending = new UniTaskCompletionSource<bool>();
            var fallback = new FakeFallback();
            var module = Create(_ => pending.Task, fallback, life, new FakeSource { Pending = true });
            try
            {
                module.Tick(0f, 0f);
                life.NotifyPause();
                if (resumeBeforeCompletion) life.NotifyResume();
                pending.TrySetResult(false);

                Assert.That(fallback.Calls, Is.Zero);
                Assert.That(module.LastRoute, Is.EqualTo(BackRoute.Unhandled));
            }
            finally { module.ShutdownAsync(default).GetAwaiter().GetResult(); }
        }

        [Test]
        public void Restart_DoesNotLetOldDispatchChangeNewSession()
        {
            var old = new UniTaskCompletionSource<bool>();
            var current = new UniTaskCompletionSource<bool>();
            var source = new FakeSource { Pending = true };
            var fallback = new FakeFallback();
            var life = new AppLifecycle();
            int calls = 0;
            var module = Create(_ => ++calls == 1 ? old.Task : current.Task, fallback, life, source);
            try
            {
                module.Tick(0f, 0f);
                module.ShutdownAsync(default).GetAwaiter().GetResult();
                module.InitializeAsync(default).GetAwaiter().GetResult();
                module.StartAsync(default).GetAwaiter().GetResult();
                source.Pending = true;
                module.Tick(0f, 0f);
                Assert.That(calls, Is.EqualTo(2));

                old.TrySetResult(false);
                source.Pending = true;
                module.Tick(0f, 0f);
                Assert.That(calls, Is.EqualTo(2), "旧请求不能解除当前请求的分发门禁");
                Assert.That(fallback.Calls, Is.Zero);
                current.TrySetResult(false);
                Assert.That(fallback.Calls, Is.EqualTo(1));
                Assert.That(module.BackCount, Is.EqualTo(1));
            }
            finally { module.ShutdownAsync(default).GetAwaiter().GetResult(); }
        }

        [Test]
        public void Dispatch_WhenUiThrows_RoutesUnhandled()
        {
            var fallback = new FakeFallback();
            InputModule module = Create(_ => throw new InvalidOperationException("boom"), fallback,
                sources: new IBackSource[] { new FakeSource { Pending = true } });

            Assert.DoesNotThrow(() => module.Tick(0.016f, 0.016f));
            Assert.AreEqual(BackRoute.Unhandled, module.LastRoute);
            Assert.AreEqual(0, fallback.Calls, "UI 异常时不再走兜底，避免一次按键处理两次");
        }
    }
}
