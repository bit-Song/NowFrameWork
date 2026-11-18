using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Assets;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.UI.Tests
{
    /// <summary>UI-1：打开/关闭/显隐状态机（假工厂，不碰 Prefab 与布局）。</summary>
    public class UiServiceTests
    {
        sealed class LifecycleView : UiView
        {
            public readonly List<string> Calls = new List<string>();
            public object Args;
            protected override void OnCreate() { Calls.Add("Create"); }
            protected override UniTask OnOpenAsync(object args, CancellationToken ct) { Args = args; Calls.Add("Open"); return UniTask.CompletedTask; }
            protected override void OnShowing() { Calls.Add("Showing"); }
            protected override void OnShown() { Calls.Add("Shown"); }
            protected override void OnHiding() { Calls.Add("Hiding"); }
            protected override void OnHidden() { Calls.Add("Hidden"); }
            protected override UniTask OnCloseAsync() { Calls.Add("Close"); Args = null; return UniTask.CompletedTask; }
            protected override void OnDispose() { Calls.Add("Dispose"); }
        }

        sealed class Args
        {
            public int Value;
        }

        sealed class FakeFactory : IUiViewFactory
        {
            public int Created;
            public int Released;
            public int Destroyed;
            public readonly List<UiView> Views = new List<UiView>();
            public Func<UiView, UniTask<bool>> OnRelease;
            public Action<UiView> OnDestroy;
            public Func<UniTask<UiView>> OnCreate;

            public FakeFactory()
            {
                OnCreate = () =>
                {
                    var go = new GameObject("UiView");
                    return UniTask.FromResult(go.AddComponent<UiView>());
                };
            }

            public async UniTask<UiView> CreateAsync(UiDefinition definition, Transform parent, CancellationToken ct)
            {
                Created++;
                UiView view = await OnCreate();
                Views.Add(view);
                return view;
            }

            public void Destroy(UiView view)
            {
                Destroyed++;
                OnDestroy?.Invoke(view);
                if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
            }

            public UniTask<bool> ReleaseAsync(UiView view)
            {
                Released++;
                return OnRelease == null ? UniTask.FromResult(true) : OnRelease(view);
            }
        }

        sealed class FakeTransition : IUiTransition
        {
            public int Shows;
            public int Hides;
            public Func<UniTask> OnShow;
            public Func<UniTask> OnHide;

            public UniTask ShowAsync(UiView view, CancellationToken ct)
            {
                Shows++;
                return OnShow == null ? UniTask.CompletedTask : OnShow();
            }

            public UniTask HideAsync(UiView view, CancellationToken ct)
            {
                Hides++;
                return OnHide == null ? UniTask.CompletedTask : OnHide();
            }
        }

        static readonly UiKey<Args> SettingsKey = new UiKey<Args>(1001, "Settings");

        readonly List<FakeFactory> _factories = new List<FakeFactory>();

        [SetUp]
        public void SetUp() => MainThreadGuard.BindCurrentThread();

        [TearDown]
        public void TearDown()
        {
            foreach (FakeFactory factory in _factories)
                foreach (UiView view in factory.Views)
                    if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
            _factories.Clear();
        }

        (UiService service, FakeFactory factory, FakeTransition transition) Create(bool multi = false, bool cache = false, UiCacheOptions options = null)
        {
            var factory = new FakeFactory();
            _factories.Add(factory);
            var transition = new FakeTransition();
            var definitions = new List<UiDefinition>
            {
                new UiDefinition(SettingsKey.Id, "Settings", AssetKey.From("Assets/UI/Settings.prefab"))
                {
                    Layer = UiLayer.Page,
                    MultiInstance = multi,
                    CacheOnClose = cache,
                },
            };

            return (new UiService(factory, definitions, transition, cacheOptions: options), factory, transition);
        }

        [Test]
        public void Cache_ReopenUsesSameInstanceWithNewSessionAndArgs()
        {
            var (service, factory, _) = Create(cache: true);
            factory.OnCreate = () => UniTask.FromResult<UiView>(new GameObject("Cached view").AddComponent<LifecycleView>());
            var first = service.OpenAsync(SettingsKey, new Args { Value = 1 }).GetAwaiter().GetResult().Handle;
            var view = (LifecycleView)factory.Views[0];
            CancellationToken oldSession = view.SessionToken;
            CancellationToken oldVisibility = view.VisibleToken;
            service.CloseAsync(first).GetAwaiter().GetResult();

            Assert.IsFalse(view.gameObject.activeSelf);
            Assert.IsTrue(oldSession.IsCancellationRequested);
            Assert.IsTrue(oldVisibility.IsCancellationRequested);
            Assert.IsNull(view.Args);
            Assert.AreEqual(0, service.SessionCount);
            Assert.AreEqual(1, service.CachedCount);
            Assert.AreEqual(0, factory.Released);
            CollectionAssert.AreEqual(new[] { "Create", "Open", "Showing", "Shown", "Hiding", "Hidden", "Close" }, view.Calls);

            var args = new Args { Value = 2 };
            var second = service.OpenAsync(SettingsKey, args).GetAwaiter().GetResult().Handle;
            Assert.AreEqual(1, factory.Created);
            Assert.AreEqual(0, service.CachedCount);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(second.Session, view.Session);
            Assert.IsFalse(view.SessionToken.IsCancellationRequested);
            Assert.AreSame(args, view.Args);
            Assert.AreEqual(UiVisibilityState.Unknown, service.GetVisibilityState(first));
            service.CloseAsync(first).GetAwaiter().GetResult();
            Assert.AreEqual(UiVisibilityState.Visible, service.GetVisibilityState(second));
            service.CloseAsync(second).GetAwaiter().GetResult();
            service.TrimCacheAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, factory.Released);
            CollectionAssert.AreEqual(new[] { "Create", "Open", "Showing", "Shown", "Hiding", "Hidden", "Close",
                "Open", "Showing", "Shown", "Hiding", "Hidden", "Close", "Dispose" }, view.Calls);
        }

        [Test]
        public void Cache_CapacityEvictsOldestAndTrimLeavesOpenHiddenSessionAlone()
        {
            var (service, factory, _) = Create(multi: true, cache: true, options: new UiCacheOptions(capacity: 2));
            var first = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var second = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var third = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            service.CloseAsync(first).GetAwaiter().GetResult();
            service.CloseAsync(second).GetAwaiter().GetResult();
            service.CloseAsync(third).GetAwaiter().GetResult();
            Assert.AreEqual(2, service.CachedCount);
            Assert.AreEqual(1, factory.Released);
            Assert.IsTrue(factory.Views[0] == null);
            Assert.IsTrue(factory.Views[1] != null);

            var reused = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            Assert.AreEqual(reused.Session, factory.Views[2].Session);
            service.HideAsync(reused).GetAwaiter().GetResult();
            service.TrimCacheAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, service.CachedCount);
            Assert.AreEqual(UiVisibilityState.Hidden, service.GetVisibilityState(reused));
            Assert.AreEqual(2, factory.Released);
            service.ShutdownAsync().GetAwaiter().GetResult();
            Assert.AreEqual(3, factory.Released);
        }

        [Test]
        public void Cache_CanceledTrimKeepsCleanupAndShutdownWaitsForSameRelease()
        {
            var (service, factory, _) = Create(cache: true);
            var handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            service.CloseAsync(handle).GetAwaiter().GetResult();
            var release = new UniTaskCompletionSource<bool>();
            factory.OnRelease = _ => release.Task;
            using (var caller = new CancellationTokenSource())
            {
                var trim = service.TrimCacheAsync(caller.Token);
                caller.Cancel();
                Assert.Throws<OperationCanceledException>(() => trim.GetAwaiter().GetResult());
                var shutdown = service.ShutdownAsync();
                Assert.AreEqual(UniTaskStatus.Pending, shutdown.Status);
                Assert.AreEqual(0, service.CachedCount);
                Assert.AreEqual(1, factory.Destroyed);
                release.TrySetResult(true);
                shutdown.GetAwaiter().GetResult();
                Assert.AreEqual(1, factory.Released);
            }
        }

        [Test]
        public void Cache_FailedEvictionIsNotReusedAndTrimRetriesRelease()
        {
            var (service, factory, _) = Create(cache: true);
            var first = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            service.CloseAsync(first).GetAwaiter().GetResult();
            factory.OnRelease = _ => UniTask.FromResult(false);
            Assert.Throws<AggregateException>(() => service.TrimCacheAsync().GetAwaiter().GetResult());
            Assert.AreEqual(1, service.FaultedCount);
            Assert.AreEqual(0, service.CachedCount);
            var replacement = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            Assert.AreEqual(2, factory.Created);
            factory.OnRelease = null;
            service.TrimCacheAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, service.FaultedCount);
            Assert.AreEqual(1, factory.Destroyed);
            Assert.AreEqual(2, factory.Released);
            Assert.AreEqual(UiVisibilityState.Visible, service.GetVisibilityState(replacement));
            service.ShutdownAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void Cache_ReopenFailureDestroysInstanceAndClearsSession()
        {
            var (service, factory, transition) = Create(cache: true);
            var handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            service.CloseAsync(handle).GetAwaiter().GetResult();
            transition.OnShow = () => UniTask.FromException(new InvalidOperationException("show failed"));
            Assert.Throws<InvalidOperationException>(() => service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult());
            Assert.AreEqual(0, service.SessionCount);
            Assert.AreEqual(0, service.CachedCount);
            Assert.AreEqual(1, factory.Created);
            Assert.AreEqual(1, factory.Released);
        }

        [Test]
        public void Open_CreatesView_AndReturnsHandle()
        {
            var (service, factory, transition) = Create();

            UiOpenResult result = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();

            Assert.AreEqual(UiOpenStatus.Ok, result.Status);
            Assert.IsTrue(result.Handle.IsValid);
            Assert.AreEqual(1, factory.Created);
            Assert.AreEqual(1, service.VisibleCount);
            Assert.AreEqual(1, service.SessionCount);
            Assert.AreEqual(1, transition.Shows, "打开后应播放一次显示过渡");

            service.CloseAsync(result.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void Open_SingleInstance_ReturnsAlreadyOpen()
        {
            var (service, factory, _) = Create();

            UiOpenResult first = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            UiOpenResult second = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();

            Assert.AreEqual(UiOpenStatus.AlreadyOpen, second.Status);
            Assert.AreEqual(first.Handle, second.Handle, "单实例应返回同一会话句柄");
            Assert.AreEqual(1, factory.Created, "不应重复创建");

            service.CloseAsync(first.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void Open_WhileOpening_ReturnsBusy()
        {
            var (service, factory, _) = Create();
            var pending = new UniTaskCompletionSource<UiView>();
            factory.OnCreate = () => pending.Task;

            UniTask<UiOpenResult> first = service.OpenAsync(SettingsKey, new Args());
            UiOpenResult second = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();

            Assert.AreEqual(UiOpenStatus.Busy, second.Status, "加载中再次打开应返回 Busy");
            Assert.AreEqual(1, service.OpeningCount);

            var go = new GameObject("UiView");
            pending.TrySetResult(go.AddComponent<UiView>());

            UiOpenResult opened = first.GetAwaiter().GetResult();
            Assert.AreEqual(UiOpenStatus.Ok, opened.Status);
            Assert.AreEqual(0, service.OpeningCount);

            service.CloseAsync(opened.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void Open_UnknownKey_Throws()
        {
            var (service, _, _) = Create();
            var unknown = new UiKey<Args>(9999, "Unknown");

            Assert.Throws<InvalidOperationException>(() =>
                service.OpenAsync(unknown, new Args()).GetAwaiter().GetResult());
        }

        [Test]
        public void Open_Cancelled_CleansUpSession()
        {
            var (service, factory, _) = Create();
            var pending = new UniTaskCompletionSource<UiView>();
            factory.OnCreate = () => pending.Task;

            var cts = new CancellationTokenSource();
            UniTask<UiOpenResult> opening = service.OpenAsync(SettingsKey, new Args(), cts.Token);

            cts.Cancel();

            // 假工厂不响应取消：模拟"取消之后实例才晚到"，验证它被回收而不是变成孤儿
            var go = new GameObject("LateUiView");
            pending.TrySetResult(go.AddComponent<UiView>());

            Assert.Throws<OperationCanceledException>(() => opening.GetAwaiter().GetResult());
            Assert.AreEqual(1, factory.Released, "晚到实例必须被销毁");
            Assert.AreEqual(0, service.SessionCount, "取消的会话必须清理");
            Assert.AreEqual(0, service.VisibleCount);
        }

        [Test]
        public void Close_ReleasesView_AndClearsCounters()
        {
            var (service, factory, _) = Create();

            UiOpenResult result = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            service.CloseAsync(result.Handle).GetAwaiter().GetResult();

            Assert.AreEqual(1, factory.Released);
            Assert.AreEqual(0, service.VisibleCount);
            Assert.AreEqual(0, service.SessionCount);
        }

        [Test]
        public void ShowHide_DelegateToTransition()
        {
            var (service, _, transition) = Create();

            UiOpenResult result = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            service.HideAsync(result.Handle).GetAwaiter().GetResult();
            service.ShowAsync(result.Handle).GetAwaiter().GetResult();

            Assert.AreEqual(1, transition.Hides);
            Assert.AreEqual(2, transition.Shows, "打开一次 + 显式 Show 一次");
            Assert.AreEqual(1, service.VisibleCount);

            service.CloseAsync(result.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void StaleHandle_IsIgnored()
        {
            var (service, factory, _) = Create();

            UiOpenResult result = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            service.CloseAsync(result.Handle).GetAwaiter().GetResult();

            // 旧句柄再次关闭：应被忽略，不产生第二次释放
            service.CloseAsync(result.Handle).GetAwaiter().GetResult();

            Assert.AreEqual(1, factory.Released);
        }

        [Test]
        public void ForeignHandle_CannotCloseOrChangeAnotherService()
        {
            var (first, _, _) = Create();
            var (second, factory, transition) = Create();
            UiHandle a = first.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            UiHandle b = second.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            Assert.AreNotEqual(a.Owner, b.Owner, "每个服务必须有独立身份");
            second.CloseAsync(a).GetAwaiter().GetResult();
            second.HideAsync(a).GetAwaiter().GetResult();
            second.ShowAsync(a).GetAwaiter().GetResult();
            second.CloseAsync(new UiHandle(a.Owner, b.Id, b.Session)).GetAwaiter().GetResult();
            Assert.AreEqual(0, factory.Released);
            Assert.AreEqual(0, transition.Hides);
            Assert.AreEqual(1, transition.Shows);
            Assert.AreEqual(1, second.SessionCount);
            first.ShutdownAsync().GetAwaiter().GetResult();
            second.ShutdownAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void Close_ReleaseTimeout_RetainsDestroyedViewForRetry()
        {
            var (service, factory, _) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            factory.OnRelease = view => UniTask.FromResult(false);
            Assert.Throws<TimeoutException>(() => service.CloseAsync(handle).GetAwaiter().GetResult());
            Assert.AreEqual(1, service.SessionCount);
            Assert.AreEqual(1, service.FaultedCount);
            Assert.AreEqual(0, service.VisibleCount);
            Assert.IsTrue(factory.Views[0] == null, "已销毁的 Unity 对象仍可能有未归还的租约");
            Assert.AreEqual(UiOpenStatus.Busy,
                service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Status);
            factory.OnRelease = null;
            service.CloseAsync(handle).GetAwaiter().GetResult();
            Assert.AreEqual(2, factory.Released, "重试必须处理 Unity 伪空的原视图");
            Assert.AreEqual(1, factory.Destroyed, "已受理的销毁不重复发起");
            Assert.AreEqual(0, service.SessionCount);
            Assert.AreEqual(0, service.FaultedCount);
        }

        [Test]
        public void Close_ReleaseThrows_CanRetryWithoutCreatingReplacement()
        {
            var (service, factory, _) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            factory.OnRelease = view => throw new InvalidOperationException("release failed");
            Assert.Throws<InvalidOperationException>(() => service.CloseAsync(handle).GetAwaiter().GetResult());
            Assert.AreEqual(1, service.FaultedCount);
            Assert.AreEqual(UiOpenStatus.Busy,
                service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Status);
            Assert.AreEqual(1, factory.Created);
            factory.OnRelease = null;
            service.CloseAsync(handle).GetAwaiter().GetResult();
            Assert.AreEqual(2, factory.Released);
            Assert.AreEqual(0, service.FaultedCount);
            Assert.AreEqual(0, service.SessionCount);
        }

        [Test]
        public void Close_DestroyThrows_DoesNotReleaseLeaseAndCanRetry()
        {
            var (service, factory, _) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            factory.OnDestroy = view => throw new InvalidOperationException("destroy failed");
            Assert.Throws<InvalidOperationException>(() => service.CloseAsync(handle).GetAwaiter().GetResult());
            Assert.AreEqual(0, factory.Released, "销毁尚未受理时不应归还租约");
            Assert.AreEqual(1, service.FaultedCount);
            factory.OnDestroy = null;
            service.CloseAsync(handle).GetAwaiter().GetResult();
            Assert.AreEqual(1, factory.Released);
            Assert.AreEqual(0, service.SessionCount);
        }

        [Test]
        public void Close_ConcurrentWaiters_WaitForSameCleanup()
        {
            var (service, factory, _) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var release = new UniTaskCompletionSource<bool>();
            factory.OnRelease = view => release.Task;
            UniTask first = service.CloseAsync(handle);
            UniTask second = service.CloseAsync(handle);
            Assert.AreEqual(UniTaskStatus.Pending, first.Status);
            Assert.AreEqual(UniTaskStatus.Pending, second.Status, "重复关闭不能提前报告完成");
            Assert.AreEqual(1, factory.Released);
            release.TrySetResult(true);
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();
            Assert.AreEqual(0, service.SessionCount);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Close_CancelOneWaiter_DoesNotCancelSharedCleanup(bool cancelFirst)
        {
            var (service, factory, _) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var release = new UniTaskCompletionSource<bool>();
            factory.OnRelease = view => release.Task;
            using (var cancellation = new CancellationTokenSource())
            {
                UniTask first = service.CloseAsync(handle, cancelFirst ? cancellation.Token : default);
                UniTask second = service.CloseAsync(handle, cancelFirst ? default : cancellation.Token);
                cancellation.Cancel();
                UniTask canceled = cancelFirst ? first : second;
                UniTask remaining = cancelFirst ? second : first;
                Assert.Throws<OperationCanceledException>(() => canceled.GetAwaiter().GetResult());
                Assert.AreEqual(UniTaskStatus.Pending, remaining.Status);
                Assert.AreEqual(1, service.SessionCount);
                release.TrySetResult(true);
                remaining.GetAwaiter().GetResult();
                Assert.AreEqual(1, factory.Released);
                Assert.AreEqual(0, service.SessionCount);
            }
        }

        [Test]
        public void Open_WhileClosing_IsBusyUntilReleaseCompletes()
        {
            var (service, factory, _) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var release = new UniTaskCompletionSource<bool>();
            factory.OnRelease = view => release.Task;
            UniTask closing = service.CloseAsync(handle);
            UiOpenResult busy = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            Assert.AreEqual(UiOpenStatus.Busy, busy.Status);
            Assert.IsFalse(busy.Handle.IsValid);
            Assert.AreEqual(1, factory.Created);
            release.TrySetResult(true);
            closing.GetAwaiter().GetResult();
            factory.OnRelease = null;
            UiOpenResult reopened = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            Assert.AreEqual(UiOpenStatus.Ok, reopened.Status);
            service.CloseAsync(handle).GetAwaiter().GetResult();
            Assert.AreEqual(1, service.SessionCount, "旧句柄不能关闭重新打开的实例");
            service.CloseAsync(reopened.Handle).GetAwaiter().GetResult();
        }

        [Test]
        public void HideAndClose_OnlyChangeTheirOwnVisibilityCount()
        {
            var (service, _, _) = Create(multi: true);
            UiHandle first = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            UiHandle second = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            service.HideAsync(first).GetAwaiter().GetResult();
            service.HideAsync(first).GetAwaiter().GetResult();
            Assert.AreEqual(1, service.VisibleCount);
            service.CloseAsync(first).GetAwaiter().GetResult();
            Assert.AreEqual(1, service.VisibleCount, "关闭已隐藏项不能扣除另一个实例的计数");
            service.CloseAsync(second).GetAwaiter().GetResult();
            Assert.AreEqual(0, service.VisibleCount);
        }

        [Test]
        public void Open_CanceledDuringShow_RollsBackEvenIfTransitionIgnoresToken()
        {
            var (service, factory, transition) = Create();
            var showing = new UniTaskCompletionSource();
            transition.OnShow = () => showing.Task;
            using (var cancellation = new CancellationTokenSource())
            {
                UniTask<UiOpenResult> opening = service.OpenAsync(SettingsKey, new Args(), cancellation.Token);
                cancellation.Cancel();
                showing.TrySetResult();
                Assert.Throws<OperationCanceledException>(() => opening.GetAwaiter().GetResult());
            }
            Assert.AreEqual(1, factory.Destroyed);
            Assert.AreEqual(1, factory.Released);
            Assert.AreEqual(0, service.OpeningCount);
            Assert.AreEqual(0, service.VisibleCount);
            Assert.AreEqual(0, service.SessionCount);
            Assert.AreEqual(0, service.FaultedCount);
        }

        [Test]
        public void Open_RollbackFails_PreservesBothErrorsAndShutdownCanRetry()
        {
            var (service, factory, transition) = Create();
            var showError = new InvalidOperationException("show failed");
            var releaseError = new InvalidOperationException("release failed");
            transition.OnShow = () => throw showError;
            factory.OnRelease = view => throw releaseError;
            AggregateException error = Assert.Throws<AggregateException>(() =>
                service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult());
            Assert.AreSame(showError, error.InnerExceptions[0]);
            Assert.AreSame(releaseError, error.InnerExceptions[1]);
            Assert.AreEqual(0, service.OpeningCount);
            Assert.AreEqual(1, service.SessionCount);
            Assert.AreEqual(1, service.FaultedCount);
            factory.OnRelease = null;
            service.ShutdownAsync().GetAwaiter().GetResult();
            Assert.AreEqual(2, factory.Released);
            Assert.AreEqual(0, service.SessionCount);
            Assert.AreEqual(0, service.FaultedCount);
        }

        [Test]
        public void Shutdown_CancelsOpeningAndWaitsForLateView()
        {
            var (service, factory, transition) = Create();
            var created = new UniTaskCompletionSource<UiView>();
            factory.OnCreate = () => created.Task;
            UniTask<UiOpenResult> opening = service.OpenAsync(SettingsKey, new Args());
            UniTask shutdown = service.ShutdownAsync();
            UniTask duplicate = service.ShutdownAsync();
            Assert.AreEqual(UniTaskStatus.Pending, shutdown.Status);
            Assert.AreEqual(UniTaskStatus.Pending, duplicate.Status);
            Assert.Throws<InvalidOperationException>(() => service.OpenAsync(SettingsKey, new Args()));
            created.TrySetResult(new GameObject("LateView").AddComponent<UiView>());
            Assert.Throws<OperationCanceledException>(() => opening.GetAwaiter().GetResult());
            shutdown.GetAwaiter().GetResult();
            duplicate.GetAwaiter().GetResult();
            Assert.AreEqual(0, transition.Shows);
            Assert.AreEqual(1, factory.Destroyed);
            Assert.AreEqual(1, factory.Released);
            Assert.AreEqual(0, service.OpeningCount);
            Assert.AreEqual(0, service.SessionCount);
        }

        [Test]
        public void Shutdown_OneReleaseFails_StillCleansOthersAndCanRetry()
        {
            var (service, factory, _) = Create(multi: true);
            UiHandle first = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult();
            UiView failedView = factory.Views[0];
            factory.OnRelease = view => UniTask.FromResult(!ReferenceEquals(view, failedView));
            Assert.Throws<AggregateException>(() => service.ShutdownAsync().GetAwaiter().GetResult());
            Assert.AreEqual(2, factory.Released, "单项失败不能阻断其它实例的清理");
            Assert.AreEqual(1, service.SessionCount);
            Assert.AreEqual(1, service.FaultedCount);
            factory.OnRelease = null;
            service.ShutdownAsync().GetAwaiter().GetResult();
            service.ShutdownAsync().GetAwaiter().GetResult();
            service.CloseAsync(first).GetAwaiter().GetResult();
            Assert.AreEqual(3, factory.Released);
            Assert.AreEqual(0, service.SessionCount);
            Assert.AreEqual(0, service.FaultedCount);
        }

        [Test]
        public void Close_WaitsForInFlightVisibilityCallbackBeforeDestroying()
        {
            var (service, factory, transition) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var hiding = new UniTaskCompletionSource();
            transition.OnHide = () => hiding.Task;
            UniTask hide = service.HideAsync(handle);
            UniTask close = service.CloseAsync(handle);
            Assert.AreEqual(UniTaskStatus.Pending, close.Status);
            Assert.AreEqual(0, factory.Destroyed, "回调退出前不能销毁它仍在使用的实例");
            hiding.TrySetResult();
            Assert.Throws<OperationCanceledException>(() => hide.GetAwaiter().GetResult());
            close.GetAwaiter().GetResult();
            Assert.AreEqual(1, factory.Released);
            Assert.AreEqual(0, service.VisibleCount);
        }

        [Test]
        public void Reverse_WaitsForOldCallbackExit_AndOldResultCannotCommit()
        {
            var (service, factory, transition) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var oldHide = new UniTaskCompletionSource();
            transition.OnHide = () => oldHide.Task; // 故意忽略取消，直到测试明确确认退出。
            UniTask hiding = service.HideAsync(handle);
            UniTask showing = service.ShowAsync(handle);
            Assert.AreEqual(1, transition.Shows, "旧回调退出前不能启动新的属性写入");
            Assert.AreEqual(UniTaskStatus.Pending, showing.Status);
            oldHide.TrySetResult();
            Assert.Throws<OperationCanceledException>(() => hiding.GetAwaiter().GetResult());
            showing.GetAwaiter().GetResult();
            Assert.AreEqual(2, transition.Shows);
            Assert.AreEqual(UiVisibilityState.Visible, service.GetVisibilityState(handle));
            Assert.AreEqual(1, service.VisibleCount);
            Assert.IsTrue(factory.Views[0].VisualGroup.interactable);
            service.CloseAsync(handle).GetAwaiter().GetResult();
        }

        [Test]
        public void SameDirection_UsesSharedCompletionAndDoesNotRepeatTransition()
        {
            var (service, _, transition) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var gate = new UniTaskCompletionSource();
            transition.OnHide = () => gate.Task;
            UniTask first = service.HideAsync(handle);
            UniTask second = service.HideAsync(handle);
            Assert.AreEqual(1, transition.Hides);
            Assert.AreEqual(UniTaskStatus.Pending, second.Status);
            gate.TrySetResult(); first.GetAwaiter().GetResult(); second.GetAwaiter().GetResult();
            service.HideAsync(handle).GetAwaiter().GetResult();
            Assert.AreEqual(1, transition.Hides, "稳定隐藏也不能再通知一次 Hidden");
            Assert.AreEqual(1, service.HiddenCount);
            service.CloseAsync(handle).GetAwaiter().GetResult();
        }

        [Test]
        public void MultipleReversals_SkipQueuedOldDirection()
        {
            var (service, _, transition) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var gate = new UniTaskCompletionSource();
            transition.OnHide = () => gate.Task;
            UniTask first = service.HideAsync(handle);
            UniTask second = service.ShowAsync(handle);
            UniTask third = service.HideAsync(handle);
            gate.TrySetResult();
            Assert.Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => second.GetAwaiter().GetResult());
            third.GetAwaiter().GetResult();
            Assert.AreEqual(1, transition.Shows, "已被替代的排队 Show 根本不应启动");
            Assert.AreEqual(UiVisibilityState.Hidden, service.GetVisibilityState(handle));
            service.CloseAsync(handle).GetAwaiter().GetResult();
        }

        [Test]
        public void VisibilityWaiterCancellation_DoesNotCancelAnotherWaiter()
        {
            var (service, _, transition) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            var gate = new UniTaskCompletionSource();
            transition.OnHide = () => gate.Task;
            using (var cancellation = new CancellationTokenSource())
            {
                UniTask first = service.HideAsync(handle, cancellation.Token);
                UniTask second = service.HideAsync(handle);
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
                Assert.AreEqual(UniTaskStatus.Pending, second.Status);
                gate.TrySetResult(); second.GetAwaiter().GetResult();
            }
            Assert.AreEqual(UiVisibilityState.Hidden, service.GetVisibilityState(handle));
            service.CloseAsync(handle).GetAwaiter().GetResult();
        }

        [Test]
        public void TransitionFailure_ConvergesToHiddenAndCanShowAgain()
        {
            var (service, factory, transition) = Create();
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            transition.OnHide = () => throw new InvalidOperationException("animation failed");
            Assert.Throws<InvalidOperationException>(() => service.HideAsync(handle).GetAwaiter().GetResult());
            Assert.AreEqual(0, service.VisibleCount);
            Assert.IsFalse(factory.Views[0].VisualGroup.blocksRaycasts);
            Assert.AreEqual(UiVisibilityState.Hidden, service.GetVisibilityState(handle));
            service.ShowAsync(handle).GetAwaiter().GetResult();
            Assert.AreEqual(1, service.VisibleCount);
            transition.OnHide = null;
            service.CloseAsync(handle).GetAwaiter().GetResult();
        }

        [Test]
        public void Lifecycle_HideCancelsVisibleWork_CloseCancelsSession_AndCallbacksHaveFixedOrder()
        {
            var (service, factory, _) = Create();
            var view = new GameObject("Lifecycle").AddComponent<LifecycleView>();
            factory.OnCreate = () => UniTask.FromResult<UiView>(view);
            var args = new Args { Value = 42 };
            UiHandle handle = service.OpenAsync(SettingsKey, args).GetAwaiter().GetResult().Handle;
            Assert.AreSame(args, view.Args);
            CancellationToken visible = view.VisibleToken, session = view.SessionToken;
            service.HideAsync(handle).GetAwaiter().GetResult();
            Assert.IsTrue(visible.IsCancellationRequested);
            Assert.IsFalse(session.IsCancellationRequested);
            service.ShowAsync(handle).GetAwaiter().GetResult();
            Assert.IsFalse(view.VisibleToken.IsCancellationRequested);
            service.CloseAsync(handle).GetAwaiter().GetResult();
            Assert.IsTrue(session.IsCancellationRequested);
            CollectionAssert.AreEqual(new[] { "Create", "Open", "Showing", "Shown", "Hiding", "Hidden", "Showing", "Shown", "Hiding", "Hidden", "Close", "Dispose" }, view.Calls);
        }

        [Test]
        public void Close_AlreadyHidden_DoesNotRepeatHiddenCallbacks()
        {
            var (service, factory, _) = Create();
            var view = new GameObject("Lifecycle").AddComponent<LifecycleView>();
            factory.OnCreate = () => UniTask.FromResult<UiView>(view);
            UiHandle handle = service.OpenAsync(SettingsKey, new Args()).GetAwaiter().GetResult().Handle;
            service.HideAsync(handle).GetAwaiter().GetResult();
            service.CloseAsync(handle).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "Create", "Open", "Showing", "Shown", "Hiding", "Hidden", "Close", "Dispose" }, view.Calls);
        }
    }
}
