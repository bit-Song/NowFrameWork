using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Assets;
using NowFrameWork.Core.Threading;
using UnityEngine;

namespace NowFrameWork.UI.Tests
{
    public class UiTransitionSelectionTests
    {
        sealed class PanelTransition : UiTransitionBehaviour
        {
            public int Shows, Hides;
            public override UniTask ShowAsync(UiView view, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Shows++; return UniTask.CompletedTask; }
            public override UniTask HideAsync(UiView view, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Hides++; return UniTask.CompletedTask; }
        }
        sealed class DefaultTransition : IUiTransition
        {
            public int Shows, Hides;
            public UniTask ShowAsync(UiView view, CancellationToken ct) { Shows++; return UniTask.CompletedTask; }
            public UniTask HideAsync(UiView view, CancellationToken ct) { Hides++; return UniTask.CompletedTask; }
        }
        sealed class Factory : IUiViewFactory
        {
            public UiView View;
            public PanelTransition Transition;
            public bool Disabled;
            public UniTask<UiView> CreateAsync(UiDefinition d, Transform p, CancellationToken ct)
            {
                View = new GameObject("Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(UiView)).GetComponent<UiView>();
                Transition = View.gameObject.AddComponent<PanelTransition>(); Transition.enabled = !Disabled;
                return UniTask.FromResult(View);
            }
            public void Destroy(UiView view) { if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject); }
            public UniTask<bool> ReleaseAsync(UiView view) => UniTask.FromResult(true);
        }
        static readonly UiKey<EmptyArgs> Key = new UiKey<EmptyArgs>(1, "Panel");
        UiService _service;
        Factory _factory;
        DefaultTransition _default;
        static void Done(UniTask task) => task.GetAwaiter().GetResult();
        [SetUp] public void SetUp()
        {
            MainThreadGuard.BindCurrentThread(); _factory = new Factory(); _default = new DefaultTransition();
            _service = new UiService(_factory, new[] { new UiDefinition(1, "Panel", AssetKey.From("panel")) }, _default);
        }
        [TearDown] public void TearDown() => Done(_service.ShutdownAsync());

        [Test] public void PanelOverride_IsUsedForOpenHideShowAndClose()
        {
            var handle = _service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult().Handle;
            Done(_service.HideAsync(handle)); Done(_service.ShowAsync(handle));
            Assert.AreEqual(2, _factory.Transition.Shows); Assert.AreEqual(1, _factory.Transition.Hides);
            var transition = _factory.Transition; Done(_service.CloseAsync(handle));
            Assert.AreEqual(2, transition.Hides); Assert.AreEqual(0, _default.Shows); Assert.AreEqual(0, _default.Hides);
        }

        [Test] public void DisabledOverride_UsesModuleDefault()
        {
            _factory.Disabled = true;
            var handle = _service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult().Handle;
            Done(_service.HideAsync(handle)); Done(_service.ShowAsync(handle));
            Assert.AreEqual(0, _factory.Transition.Shows); Assert.AreEqual(2, _default.Shows); Assert.AreEqual(1, _default.Hides);
        }

        [Test] public void Selection_IsPinnedForTheSession()
        {
            var handle = _service.OpenAsync(Key, EmptyArgs.Default).GetAwaiter().GetResult().Handle;
            _factory.Transition.enabled = false;
            Done(_service.HideAsync(handle)); Assert.AreEqual(1, _factory.Transition.Hides); Assert.AreEqual(0, _default.Hides);
        }
    }
}
