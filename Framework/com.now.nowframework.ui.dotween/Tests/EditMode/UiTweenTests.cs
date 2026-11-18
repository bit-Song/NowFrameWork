using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using NUnit.Framework;
using NowFrameWork.Assets;
using NowFrameWork.Core.Threading;
using UnityEngine;
using TweenEngine = DG.Tweening.DOTween;

namespace NowFrameWork.UI.Tweening.Tests
{
    public class UiTweenTests
    {
        sealed class ManualTransition : DOTweenUiTransition
        {
            protected override UpdateType TweenUpdateType => UpdateType.Manual;
        }
        sealed class Factory : IUiViewFactory
        {
            readonly UiView _view;
            readonly ManualTransition _transition;
            public int ReleaseCount;
            public bool TweenPlayingAtDestroy;

            public Factory(UiView view, ManualTransition transition) { _view = view; _transition = transition; }
            public UniTask<UiView> CreateAsync(UiDefinition definition, Transform parent, CancellationToken ct) => UniTask.FromResult(_view);
            public void Destroy(UiView view)
            {
                TweenPlayingAtDestroy = _transition.IsPlaying;
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
            public UniTask<bool> ReleaseAsync(UiView view)
            {
                ReleaseCount++;
                return UniTask.FromResult(view == null);
            }
        }
        readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        readonly List<UniTask> _tasks = new List<UniTask>();
        UiView _view;
        RectTransform _motion;
        ManualTransition _transition;
        UiTweenPreset _preset;
        Tween _foreign;
        bool _wasInitialized;
        static readonly FieldInfo Initialized = typeof(TweenEngine).GetField("initialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        static UiTweenPhase Phase(Vector2 offset) => new UiTweenPhase
        {
            Channels = UiTweenChannels.Fade | UiTweenChannels.Move | UiTweenChannels.Scale,
            Duration = 0.2f, Ease = Ease.Linear, Alpha = 0, Offset = offset, Scale = new Vector3(0.5f, 0.5f, 1)
        };
        UiView CreateView(string name, out RectTransform motion, out ManualTransition transition)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(UiView)); _objects.Add(go);
            var child = new GameObject("MotionRoot", typeof(RectTransform)); child.transform.SetParent(go.transform, false);
            motion = (RectTransform)child.transform; motion.anchoredPosition = new Vector2(20, 30); motion.localScale = new Vector3(2, 2, 1);
            go.GetComponent<CanvasGroup>().alpha = 0.8f;
            transition = go.AddComponent<ManualTransition>(); return go.GetComponent<UiView>();
        }
        [SetUp] public void SetUp()
        {
            MainThreadGuard.BindCurrentThread();
            // DOTween.Init 在非 Play 模式下不设置 initialized，但 ManualUpdate 仍可创建/驱动 Tween。
            // 本手动宿主不创建全局驱动对象，显式满足 Kill 的初始化前提，退出时恢复原值。
            _wasInitialized = (bool)Initialized.GetValue(null); Initialized.SetValue(null, true);
            _preset = ScriptableObject.CreateInstance<UiTweenPreset>(); _objects.Add(_preset);
            _preset.Enter = Phase(new Vector2(100, 0)); _preset.Exit = Phase(new Vector2(-100, 0));
            _view = CreateView("View", out _motion, out _transition); _transition.Configure(_preset, _motion);
        }
        [TearDown] public void TearDown()
        {
            if (_foreign != null && _foreign.IsActive()) _foreign.Kill(false); _foreign = null;
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
            foreach (var task in _tasks) { try { task.GetAwaiter().GetResult(); } catch (Exception) { } }
            TweenEngine.ManualUpdate(0, 0); Initialized.SetValue(null, _wasInitialized);
            _objects.Clear(); _tasks.Clear();
        }
        UniTask Track(UniTask task) { var shared = task.Preserve(); _tasks.Add(shared); return shared; }
        static void Done(UniTask task) => task.GetAwaiter().GetResult();
        static void Advance(float seconds = 1) => TweenEngine.ManualUpdate(0, seconds);
        void Show() { var task = Track(_transition.ShowAsync(_view, default)); Advance(); Done(task); }
        void Hide() { var task = Track(_transition.HideAsync(_view, default)); Advance(); Done(task); }
        void AssertShown()
        {
            Assert.That(_view.VisualGroup.alpha, Is.EqualTo(0.8f).Within(0.0001));
            Assert.AreEqual(new Vector2(20, 30), _motion.anchoredPosition); Assert.AreEqual(new Vector3(2, 2, 1), _motion.localScale);
            Assert.IsFalse(_transition.IsPlaying);
        }

        [Test] public void CombinedEnterExit_UsesUnscaledTime_AndEndsAtOwnedPose()
        {
            var show = Track(_transition.ShowAsync(_view, default));
            Assert.AreEqual(new Vector2(120, 30), _motion.anchoredPosition); Assert.AreEqual(0, _view.VisualGroup.alpha);
            Advance(); Done(show); AssertShown();
            Hide(); Assert.AreEqual(new Vector2(-80, 30), _motion.anchoredPosition); Assert.AreEqual(0, _view.VisualGroup.alpha);
        }

        [Test] public void CancelHide_StopsOnlyOwnedTween_AndReverseDoesNotJump()
        {
            Show(); float foreignValue = 0;
            _foreign = TweenEngine.To(() => foreignValue, x => foreignValue = x, 1, 10).SetUpdate(UpdateType.Manual, true).SetAutoKill(false);
            using (var cancellation = new CancellationTokenSource())
            {
                var hide = Track(_transition.HideAsync(_view, cancellation.Token)); Advance(0.1f);
                Vector2 position = _motion.anchoredPosition; float alpha = _view.VisualGroup.alpha;
                cancellation.Cancel(); Assert.Throws<OperationCanceledException>(() => Done(hide));
                Assert.IsFalse(_transition.IsPlaying, "取消任务返回之前必须杀掉底层 Tween");
                var remaining = TweenEngine.TweensByTarget(_transition, false);
                if (remaining != null) foreach (var tween in remaining) Assert.IsFalse(tween.IsActive(), "取消后不允许本面板 Tween 继续活动");
                Advance(0.05f); Assert.AreEqual(position, _motion.anchoredPosition); Assert.AreEqual(alpha, _view.VisualGroup.alpha);
                Assert.IsTrue(_foreign.IsActive()); Assert.Greater(foreignValue, 0);
                var show = Track(_transition.ShowAsync(_view, default));
                Assert.AreEqual(position, _motion.anchoredPosition); Assert.AreEqual(alpha, _view.VisualGroup.alpha);
                Advance(); Done(show); AssertShown();
            }
        }

        [Test] public void StableHidden_ShowUsesEnterStartRatherThanExitEnd()
        {
            Show(); Hide(); var show = Track(_transition.ShowAsync(_view, default));
            Assert.AreEqual(new Vector2(120, 30), _motion.anchoredPosition);
            Advance(); Done(show); AssertShown();
        }

        [Test] public void ZeroDuration_AppliesPoseWithoutLeavingAnActiveTween()
        {
            var phase = _preset.Enter; phase.Duration = 0; _preset.Enter = phase;
            Done(Track(_transition.ShowAsync(_view, default))); AssertShown();
        }

        [Test] public void Delay_HoldsConfiguredStart_ThenFinishes()
        {
            var phase = _preset.Enter; phase.Delay = 0.3f; _preset.Enter = phase;
            var show = Track(_transition.ShowAsync(_view, default)); Advance(0.1f);
            Assert.AreEqual(0, _view.VisualGroup.alpha); Assert.AreEqual(new Vector2(120, 30), _motion.anchoredPosition);
            Advance(); Done(show); AssertShown();
        }

        [Test] public void SharedPreset_HasIndependentInstances_AndCapturesValuesOnce()
        {
            var secondView = CreateView("Second", out var secondMotion, out var secondTransition);
            secondMotion.anchoredPosition = new Vector2(5, 6); secondTransition.Configure(_preset, secondMotion);
            var first = Track(_transition.ShowAsync(_view, default)); var second = Track(secondTransition.ShowAsync(secondView, default));
            var changed = _preset.Exit; changed.Offset = new Vector2(999, 999); _preset.Exit = changed;
            Advance(); Done(first); Done(second);
            Hide(); Assert.AreEqual(new Vector2(-80, 30), _motion.anchoredPosition);
            Assert.AreEqual(new Vector2(5, 6), secondMotion.anchoredPosition); Assert.That(secondView.VisualGroup.alpha, Is.EqualTo(0.8f).Within(0.0001));
        }

        [Test] public void FadeOnContentGroup_DoesNotKeepRootInvisible()
        {
            var group = _motion.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0.6f;
            _transition.Configure(_preset, _motion, group);
            var show = Track(_transition.ShowAsync(_view, default));
            Assert.AreEqual(0.8f, _view.VisualGroup.alpha); Assert.AreEqual(0, group.alpha);
            Advance(); Done(show); Assert.That(group.alpha, Is.EqualTo(0.6f).Within(0.0001));
        }

        [Test] public void EnterWithoutMoveOrScale_RestoresChannelsOwnedByExit()
        {
            var phase = _preset.Enter; phase.Channels = UiTweenChannels.Fade; _preset.Enter = phase;
            Show(); Hide(); Show(); AssertShown();
        }

        [Test] public void MissingOrForeignMotionRoot_IsRejectedBeforePlaying()
        {
            _transition.Configure(_preset);
            Assert.Throws<InvalidOperationException>(() => Done(Track(_transition.ShowAsync(_view, default))));
            Assert.IsFalse(_transition.IsPlaying);
            _transition.Configure(_preset, (RectTransform)_view.transform);
            Assert.Throws<InvalidOperationException>(() => Done(Track(_transition.ShowAsync(_view, default))));
        }

        [Test] public void InvalidTimingAndCurve_AreRejected()
        {
            var phase = _preset.Enter; phase.Duration = float.NaN; _preset.Enter = phase;
            Assert.Throws<ArgumentException>(() => Done(Track(_transition.ShowAsync(_view, default))));
            phase.Duration = 0.2f; phase.UseCustomCurve = true; phase.Curve = AnimationCurve.Linear(0, 0, 1, 0); _preset.Enter = phase;
            Assert.Throws<ArgumentException>(() => Done(Track(_transition.ShowAsync(_view, default))));
        }

        [Test] public void CustomCurve_IsCopiedRatherThanSharedWithLiveAssetEdits()
        {
            var phase = _preset.Enter; phase.UseCustomCurve = true; phase.Curve = AnimationCurve.Linear(0, 0, 1, 1); _preset.Enter = phase;
            var show = Track(_transition.ShowAsync(_view, default));
            phase.Curve.keys = AnimationCurve.Linear(0, 0, 1, 0).keys;
            Advance(); Done(show); AssertShown();
        }

        [Test] public void ExternalKill_IsReportedAndDoesNotLeaveAnAwaitHanging()
        {
            var show = Track(_transition.ShowAsync(_view, default)); TweenEngine.Kill(_transition, false);
            Assert.Throws<InvalidOperationException>(() => Done(show)); Assert.IsFalse(_transition.IsPlaying);
        }

        [Test]
        public void Cache_ReopenUsesFreshPresetSnapshotAndRestoredMotionPose()
        {
            var factory = new Factory(_view, _transition);
            var definition = new UiDefinition(1, "Panel", AssetKey.From("panel")) { CacheOnClose = true };
            var service = new UiService(factory, new[] { definition });
            var key = new UiKey<EmptyArgs>(1, "Panel");
            var first = service.OpenAsync(key, EmptyArgs.Default);
            Advance();
            var close = Track(service.CloseAsync(first.GetAwaiter().GetResult().Handle));
            Advance();
            Done(close);
            Assert.AreEqual(new Vector2(20, 30), _motion.anchoredPosition);
            Assert.AreEqual(new Vector3(2, 2, 1), _motion.localScale);

            _preset.Enter = Phase(new Vector2(50, 0));
            var second = service.OpenAsync(key, EmptyArgs.Default);
            Assert.AreEqual(new Vector2(70, 30), _motion.anchoredPosition);
            Advance();
            Assert.AreEqual(UiOpenStatus.Ok, second.GetAwaiter().GetResult().Status);
            AssertShown();
            Done(service.ShutdownAsync());
            Assert.AreEqual(1, factory.ReleaseCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Shutdown_ReleasesViewWhileDotweenClockIsPaused(bool closeAlreadyStarted)
        {
            var factory = new Factory(_view, _transition);
            var service = new UiService(factory, new[] { new UiDefinition(1, "Panel", AssetKey.From("panel")) });
            var open = service.OpenAsync(new UiKey<EmptyArgs>(1, "Panel"), EmptyArgs.Default);
            Advance();
            var handle = open.GetAwaiter().GetResult().Handle;
            float savedTimeScale = TweenEngine.timeScale;
            UniTask close = UniTask.CompletedTask;
            UniTask shutdown = UniTask.CompletedTask;
            try
            {
                TweenEngine.timeScale = 0;
                if (closeAlreadyStarted)
                {
                    close = service.CloseAsync(handle).Preserve();
                    Assert.AreEqual(UniTaskStatus.Pending, close.Status);
                    Assert.IsTrue(_transition.IsPlaying);
                }

                shutdown = service.ShutdownAsync().Preserve();
                Assert.AreEqual(UniTaskStatus.Succeeded, shutdown.Status);
                Assert.AreEqual(UniTaskStatus.Succeeded, close.Status);
                Assert.AreEqual(0, service.SessionCount);
                Assert.AreEqual(1, factory.ReleaseCount);
                Assert.IsFalse(factory.TweenPlayingAtDestroy);
                Assert.AreEqual(0, TweenEngine.timeScale);
            }
            finally
            {
                TweenEngine.timeScale = savedTimeScale;
                Advance();
                Done(shutdown);
                Done(close);
            }
        }

        [Test] public void CanceledBeforeStart_DoesNotChangePose()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); Assert.Throws<OperationCanceledException>(() => Done(Track(_transition.ShowAsync(_view, cancellation.Token))));
                AssertShown();
            }
        }
    }
}
