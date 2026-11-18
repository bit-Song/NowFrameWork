using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Assets;
using NowFrameWork.Core.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NowFrameWork.UI.Tests
{
    public class UiWindowTests
    {
        sealed class Factory : IUiViewFactory
        {
            public readonly List<UiView> Views = new List<UiView>();
            public bool FailRelease;
            public Action<UiView> Configure;
            public UniTask<UiView> CreateAsync(UiDefinition d, Transform parent, CancellationToken ct)
            {
                var go = new GameObject(d.Name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster), typeof(UiView));
                var view = go.GetComponent<UiView>(); Configure?.Invoke(view); Views.Add(view); return UniTask.FromResult(view);
            }
            public void Destroy(UiView view) { if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject); }
            public UniTask<bool> ReleaseAsync(UiView view) => UniTask.FromResult(!FailRelease);
        }
        sealed class Transition : IUiTransition
        {
            public Func<UiView, CancellationToken, UniTask> Show;
            public Func<UiView, CancellationToken, UniTask> Hide;
            public UniTask ShowAsync(UiView view, CancellationToken ct) => Show == null ? UniTask.CompletedTask : Show(view, ct);
            public UniTask HideAsync(UiView view, CancellationToken ct) => Hide == null ? UniTask.CompletedTask : Hide(view, ct);
        }
        Factory _factory;
        readonly List<UiService> _services = new List<UiService>();
        GameObject _eventSystem;
        static UiKey<EmptyArgs> Key(int id) => new UiKey<EmptyArgs>(id, "Window" + id);
        static UiDefinition Def(int id, UiLayer layer = UiLayer.Page, bool modal = false) =>
            new UiDefinition(id, "Window" + id, AssetKey.From("panel")) { Layer = layer, Modal = modal };
        static void Done(UniTask task) => task.GetAwaiter().GetResult();
        static T Done<T>(UniTask<T> task) => task.GetAwaiter().GetResult();
        UiService Service(UiDefinition[] definitions = null, IUiTransition transition = null)
        {
            var service = new UiService(_factory, definitions ?? new[] { Def(1), Def(2), Def(3, UiLayer.Popup, true), Def(4, UiLayer.Popup, true), Def(5, UiLayer.Toast) }, transition);
            _services.Add(service); return service;
        }
        static UiHandle Open(UiService service, int id) => Done(service.OpenAsync(Key(id), EmptyArgs.Default)).Handle;

        [SetUp] public void SetUp() { MainThreadGuard.BindCurrentThread(); _factory = new Factory(); }
        [TearDown] public void TearDown()
        {
            _factory.FailRelease = false;
            foreach (var service in _services) Done(service.ShutdownAsync());
            _services.Clear();
            foreach (var view in _factory.Views) if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
            if (_eventSystem != null)
            {
                typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_eventSystem.GetComponent<EventSystem>(), null);
                UnityEngine.Object.DestroyImmediate(_eventSystem);
            }
        }

        [Test] public void Modal_BlocksAllLowerLayers_ToastDoesNotTakeInputOrBack()
        {
            var service = Service(); var page = Open(service, 1); var first = Open(service, 3); var top = Open(service, 4); Open(service, 5);
            Assert.IsTrue(_factory.Views[0].InputBlocked); Assert.IsTrue(_factory.Views[1].InputBlocked);
            Assert.IsFalse(_factory.Views[2].InputBlocked); Assert.IsTrue(_factory.Views[3].InputBlocked);
            Assert.AreEqual(top, service.TopModal);
            Assert.IsTrue(Done(service.RequestBackAsync()));
            Assert.AreEqual(first, service.TopModal); Assert.IsFalse(_factory.Views[1].InputBlocked);
            Done(service.CloseAsync(first)); Assert.IsFalse(_factory.Views[0].InputBlocked); Assert.IsFalse(service.TopModal.IsValid);
        }

        [Test] public void Sorting_UsesLayerThenWindowOrder_PreservesChildRelativeOffsets()
        {
            _factory.Configure = view =>
            {
                var child = new GameObject("Independent", typeof(RectTransform), typeof(Canvas)); child.transform.SetParent(view.transform, false);
                var canvas = child.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 7;
            };
            var service = Service(); Open(service, 3); Open(service, 1); Open(service, 5);
            var popup = _factory.Views[0].GetComponent<Canvas>(); var page = _factory.Views[1].GetComponent<Canvas>();
            Assert.Greater(popup.sortingOrder, page.sortingOrder);
            Assert.AreEqual(popup.sortingOrder + 7, _factory.Views[0].transform.GetChild(0).GetComponent<Canvas>().sortingOrder);
            Assert.Greater(_factory.Views[2].GetComponent<Canvas>().sortingOrder, popup.sortingOrder);
        }

        [Test] public void InvalidChildCanvasOrder_FailsOpenAndReleasesSession()
        {
            _factory.Configure = view =>
            {
                var child = new GameObject("Escaped", typeof(Canvas)); child.transform.SetParent(view.transform);
                child.GetComponent<Canvas>().overrideSorting = true; child.GetComponent<Canvas>().sortingOrder = 100;
            };
            var service = Service(); Assert.Throws<InvalidOperationException>(() => Open(service, 1)); Assert.AreEqual(0, service.SessionCount);
        }

        [Test] public void ChildGroupBypassingInputScope_IsRejectedAndReleased()
        {
            _factory.Configure = view =>
            {
                var child = new GameObject("Escaped input", typeof(CanvasGroup)); child.transform.SetParent(view.transform);
                child.GetComponent<CanvasGroup>().ignoreParentGroups = true;
            };
            var service = Service(); Assert.Throws<InvalidOperationException>(() => Open(service, 1)); Assert.AreEqual(0, service.SessionCount);
        }

        [Test] public void Definitions_AreFrozen_AndInvalidModalConfigurationRejected()
        {
            var definition = Def(1); var service = Service(new[] { definition }); definition.Layer = UiLayer.Toast;
            Open(service, 1); Assert.IsFalse(_factory.Views[0].InputBlocked);
            Assert.Throws<ArgumentException>(() => Service(new[] { Def(2, UiLayer.Page, true) }));
            var invalid = Def(3, UiLayer.Popup, true); invalid.BackBehavior = UiBackBehavior.Ignore;
            Assert.Throws<ArgumentException>(() => Service(new[] { invalid }));
        }

        [Test] public void Modal_StaysBlockingUntilHideCompletes_ThenShowRestoresIt()
        {
            var gate = new UniTaskCompletionSource(); var transition = new Transition { Hide = (_, __) => gate.Task };
            var service = Service(transition: transition); Open(service, 1); var modal = Open(service, 3);
            var hide = service.HideAsync(modal);
            Assert.IsTrue(_factory.Views[0].InputBlocked); Assert.IsTrue(_factory.Views[1].InputBlocked);
            Assert.IsTrue(Done(service.RequestBackAsync())); Assert.AreEqual(2, service.SessionCount);
            gate.TrySetResult(); Done(hide); Assert.IsFalse(_factory.Views[0].InputBlocked);
            Done(service.ShowAsync(modal)); Assert.IsTrue(_factory.Views[0].InputBlocked);
        }

        [Test] public void BackBlock_DoesNotCloseUnderlyingWindow_IgnoreSkipsNonModal()
        {
            var blocker = Def(3, UiLayer.Popup, true); blocker.BackBehavior = UiBackBehavior.Block;
            var ignored = Def(4, UiLayer.Popup); ignored.BackBehavior = UiBackBehavior.Ignore;
            var service = Service(new[] { Def(1), blocker, ignored }); Open(service, 1); var modal = Open(service, 3); Open(service, 4);
            Assert.IsTrue(Done(service.RequestBackAsync())); Assert.AreEqual(3, service.SessionCount);
            Assert.AreEqual(modal, service.TopModal);
        }

        [Test] public void ClosingTopModal_RestoresOnlyValidPreviousFocus()
        {
            _eventSystem = new GameObject("Events", typeof(EventSystem));
            // EditMode 不自动派发生命周期，显式登记，与运行时 OnEnable 的注册行为一致。
            typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_eventSystem.GetComponent<EventSystem>(), null);
            EventSystem.current = _eventSystem.GetComponent<EventSystem>();
            _factory.Configure = view =>
            {
                var button = new GameObject("Focus", typeof(RectTransform), typeof(Image), typeof(Button)); button.transform.SetParent(view.transform);
                view.DefaultFocus = button.GetComponent<Button>();
            };
            var service = Service(); Open(service, 1); var selected = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(selected); var modal = Open(service, 3);
            Assert.AreNotSame(selected, EventSystem.current.currentSelectedGameObject);
            Done(service.CloseAsync(modal)); Assert.AreSame(selected, EventSystem.current.currentSelectedGameObject);
            _factory.Configure = view => view.DefaultFocus = selected.GetComponent<Selectable>();
            var invalidFocus = Open(service, 3);
            Assert.IsNull(EventSystem.current.currentSelectedGameObject, "模态窗口不得把焦点交给自己的范围外控件");
            Done(service.CloseAsync(invalidFocus)); Assert.AreSame(selected, EventSystem.current.currentSelectedGameObject);
        }

        [Test] public void Navigation_PushPop_PreservesPageInstance_AndProtectsRoot()
        {
            var service = Service(); var root = Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)).Handle;
            var rootView = _factory.Views[0]; var next = Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default)).Handle;
            Assert.AreEqual(UiVisibilityState.Hidden, service.GetVisibilityState(root)); Assert.AreEqual(2, service.Navigation.PageCount);
            Assert.IsTrue(Done(service.Navigation.BackAsync())); Assert.AreEqual(root, service.Navigation.CurrentPage);
            Assert.AreSame(rootView, _factory.Views[0]); Assert.AreEqual(UiVisibilityState.Unknown, service.GetVisibilityState(next));
            Assert.IsFalse(Done(service.Navigation.BackAsync())); Assert.AreEqual(1, service.SessionCount);
        }

        [Test] public void Navigation_BackClosesPopupBeforePoppingPage()
        {
            var service = Service(); Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)); Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default));
            Open(service, 3); Open(service, 4);
            Assert.IsTrue(Done(service.Navigation.BackAsync())); Assert.AreEqual(2, service.Navigation.PageCount);
            Assert.IsTrue(Done(service.Navigation.BackAsync())); Assert.AreEqual(2, service.Navigation.PageCount);
            Assert.IsTrue(Done(service.Navigation.BackAsync())); Assert.AreEqual(1, service.Navigation.PageCount);
        }

        [Test] public void Navigation_PopKeepsOutgoingPageAboveRestoredPageUntilExitCompletes()
        {
            var exit = new UniTaskCompletionSource();
            var transition = new Transition { Hide = (view, _) => view.UiId == 2 ? exit.Task : UniTask.CompletedTask };
            var service = Service(transition: transition);
            var previous = Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)).Handle;
            var current = Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default)).Handle;
            var previousView = _factory.Views[0];
            var currentView = _factory.Views[1];
            var pop = service.Navigation.PopAsync().Preserve();
            try
            {
                Assert.AreEqual(UniTaskStatus.Pending, pop.Status);
                Assert.AreEqual(UiVisibilityState.Visible, service.GetVisibilityState(previous));
                Assert.AreEqual(UiVisibilityState.Hiding, service.GetVisibilityState(current));
                Assert.Greater(currentView.GetComponent<Canvas>().sortingOrder, previousView.GetComponent<Canvas>().sortingOrder);
                Assert.IsTrue(previousView.InputBlocked);
                Assert.IsTrue(currentView.InputBlocked);
            }
            finally { exit.TrySetResult(); }
            Assert.IsTrue(Done(pop));
            Assert.AreEqual(previous, service.Navigation.CurrentPage);
            Assert.IsFalse(previousView.InputBlocked);
            Assert.IsTrue(currentView == null);
        }

        [Test] public void Navigation_RejectsPopupAndHistoricalDuplicate_AndDoesNotAdoptExternalPage()
        {
            var service = Service(); Assert.Throws<InvalidOperationException>(() => Done(service.Navigation.PushAsync(Key(3), EmptyArgs.Default)));
            Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)); Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default));
            Assert.Throws<InvalidOperationException>(() => Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)));
            Assert.AreEqual(UiOpenStatus.AlreadyOpen, Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default)).Status);
            var another = Service(); Open(another, 1);
            Assert.Throws<InvalidOperationException>(() => Done(another.Navigation.PushAsync(Key(1), EmptyArgs.Default)));
        }

        [Test] public void Navigation_InFlightRejectsPushAndConsumesBack_CallerCancellationDoesNotAbandonPage()
        {
            var gate = new UniTaskCompletionSource(); var transition = new Transition(); var service = Service(transition: transition);
            Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)); transition.Show = (_, __) => gate.Task;
            using (var caller = new CancellationTokenSource())
            {
                var push = service.Navigation.PushAsync(Key(2), EmptyArgs.Default, caller.Token);
                Assert.IsTrue(service.Navigation.IsBusy); Assert.IsTrue(_factory.Views[0].InputBlocked);
                Assert.Throws<InvalidOperationException>(() => service.Navigation.PushAsync(Key(1), EmptyArgs.Default));
                Assert.IsTrue(Done(service.Navigation.BackAsync())); caller.Cancel();
                Assert.Throws<OperationCanceledException>(() => Done(push));
                gate.TrySetResult(); Assert.IsFalse(service.Navigation.IsBusy); Assert.AreEqual(2, service.Navigation.PageCount);
            }
            transition.Show = null;
        }

        [Test] public void Navigation_ShowFailure_RetainsPreviousPageAndNoExtraSession()
        {
            var transition = new Transition(); var service = Service(transition: transition);
            var root = Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)).Handle;
            transition.Show = (_, __) => UniTask.FromException(new InvalidOperationException("show failed"));
            Assert.Throws<InvalidOperationException>(() => Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default)));
            Assert.AreEqual(root, service.Navigation.CurrentPage); Assert.AreEqual(1, service.SessionCount);
            transition.Show = null;
        }

        [Test] public void Navigation_CloseFailure_KeepsHistoryForRetry()
        {
            var service = Service(); Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)); Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default));
            _factory.FailRelease = true;
            Assert.Throws<TimeoutException>(() => Done(service.Navigation.PopAsync())); Assert.AreEqual(2, service.Navigation.PageCount);
            _factory.FailRelease = false;
            Assert.IsTrue(Done(service.Navigation.PopAsync())); Assert.AreEqual(1, service.Navigation.PageCount); Assert.AreEqual(1, service.SessionCount);
        }

        [Test] public void Navigation_PushRepairsIncompletePopBeforeOpeningAnotherPage()
        {
            var service = Service(new[] { Def(1), Def(2), Def(3) });
            var root = Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)).Handle;
            var outgoing = Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default)).Handle;
            _factory.FailRelease = true;
            Assert.Throws<TimeoutException>(() => Done(service.Navigation.PopAsync()));

            Assert.Throws<TimeoutException>(() => Done(service.Navigation.PushAsync(Key(3), EmptyArgs.Default)));
            Assert.AreEqual(2, _factory.Views.Count);
            Assert.AreEqual(UiVisibilityState.Visible, service.GetVisibilityState(root));
            Assert.AreEqual(1, service.VisibleCount);

            _factory.FailRelease = false;
            var next = Done(service.Navigation.PushAsync(Key(3), EmptyArgs.Default)).Handle;
            Assert.AreEqual(next, service.Navigation.CurrentPage);
            Assert.AreEqual(2, service.Navigation.PageCount);
            Assert.AreEqual(2, service.SessionCount);
            Assert.AreEqual(1, service.VisibleCount);
            Assert.AreEqual(UiVisibilityState.Hidden, service.GetVisibilityState(root));
            Assert.IsTrue(_factory.Views[0].InputBlocked);
            Assert.AreEqual(UiVisibilityState.Unknown, service.GetVisibilityState(outgoing));
        }

        [Test] public void Shutdown_CancelsAndWaitsForNavigationBeforeClosingHistory()
        {
            var gate = new UniTaskCompletionSource(); var transition = new Transition(); var service = Service(transition: transition);
            Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default));
            transition.Show = (_, ct) => gate.Task.AttachExternalCancellation(ct);
            var push = service.Navigation.PushAsync(Key(2), EmptyArgs.Default);
            Done(service.ShutdownAsync()); Assert.Throws<OperationCanceledException>(() => Done(push));
            Assert.AreEqual(0, service.SessionCount); Assert.AreEqual(0, service.Navigation.PageCount);
        }

        [Test] public void Shutdown_CancelsPopExitAndWaitsForItToStopBeforeDestroying()
        {
            var exit = new UniTaskCompletionSource();
            CancellationToken exitToken = default;
            var transition = new Transition();
            var service = Service(transition: transition);
            Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default));
            Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default));
            transition.Hide = async (_, ct) =>
            {
                exitToken = ct;
                await exit.Task;
                ct.ThrowIfCancellationRequested();
            };
            var pop = service.Navigation.PopAsync().Preserve();
            var shutdown = service.ShutdownAsync();
            try
            {
                Assert.IsTrue(exitToken.IsCancellationRequested);
                Assert.AreEqual(UniTaskStatus.Pending, shutdown.Status);
                Assert.IsTrue(_factory.Views[1] != null);
            }
            finally { exit.TrySetResult(); }
            Done(shutdown);
            Assert.Throws<OperationCanceledException>(() => Done(pop));
            Assert.AreEqual(0, service.SessionCount);
            Assert.AreEqual(0, service.Navigation.PageCount);
        }

        [Test] public void PublicBack_UsesNavigationHistory_AndModalPreventsDirectPageChanges()
        {
            var service = Service(); Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)); Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default));
            Open(service, 3);
            Assert.Throws<InvalidOperationException>(() => service.Navigation.PopAsync());
            Done(service.RequestBackAsync()); Assert.AreEqual(2, service.Navigation.PageCount);
            Done(service.RequestBackAsync()); Assert.AreEqual(1, service.Navigation.PageCount);
            Assert.IsFalse(Done(service.RequestBackAsync())); Assert.AreEqual(1, service.SessionCount);
        }

        [Test] public void FailedPushRollback_IsRetainedAndRepairedBeforeNextNavigation()
        {
            var transition = new Transition(); var service = Service(transition: transition);
            var root = Done(service.Navigation.PushAsync(Key(1), EmptyArgs.Default)).Handle;
            transition.Hide = (view, _) => view.UiId == 1 ? UniTask.FromException(new InvalidOperationException("hide failed")) : UniTask.CompletedTask;
            _factory.FailRelease = true;
            Assert.Throws<AggregateException>(() => Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default)));
            Assert.AreEqual(root, service.Navigation.CurrentPage); Assert.AreEqual(2, service.SessionCount);
            transition.Hide = null; _factory.FailRelease = false;
            Done(service.Navigation.PushAsync(Key(2), EmptyArgs.Default));
            Assert.AreEqual(2, service.Navigation.PageCount); Assert.AreEqual(2, service.SessionCount);
        }
    }
}
