using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Assets;
using NowFrameWork.Boot;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Storage;
using NowFrameWork.Core.Threading;
using NowFrameWork.UI;
using NowFrameWork.Unity;
using NowFrameWork.Unity.Storage;
using UnityEngine;

namespace Game.Tests
{
    public sealed class GameEntryTests
    {
        sealed class Provider : IAssetProvider
        {
            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct) => UniTask.FromResult(key.Address);
            public UniTask<ProviderHandle> LoadAsync(string key, CancellationToken ct) =>
                UniTask.FromException<ProviderHandle>(new InvalidOperationException("load failed"));
            public void Release(ProviderHandle handle) { }
        }

        sealed class Factory : IUiViewFactory
        {
            public UniTask<UiView> CreateAsync(UiDefinition d, Transform p, CancellationToken ct) => throw new NotSupportedException();
            public void Destroy(UiView view) => throw new NotSupportedException();
            public UniTask<bool> ReleaseAsync(UiView view) => throw new NotSupportedException();
        }

        sealed class Entry : IBootEntry
        {
            public UniTaskCompletionSource Gate;
            public bool FailAfterBind;
            public bool FailCleanup;
            public GameEntry.Binding Binding;
            public async UniTask EnterAsync(BootContext context, CancellationToken ct)
            {
                if (Gate != null) await Gate.Task.AttachExternalCancellation(ct);
                ct.ThrowIfCancellationRequested();
                Binding = GameEntry.Bind(context);
                if (FailAfterBind) throw new InvalidOperationException("business entry failed");
            }
            public UniTask ExitAsync()
            {
                Binding?.BeginStopping();
                if (FailCleanup) throw new InvalidOperationException("cleanup failed");
                Binding?.Dispose(); Binding = null;
                return UniTask.CompletedTask;
            }
        }

        readonly List<GameEntry.Binding> _bindings = new List<GameEntry.Binding>();
        string _directory;
        static void Complete(UniTask task) => task.GetAwaiter().GetResult();
        static void Reset() => typeof(GameEntry).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        [SetUp] public void SetUp()
        {
            MainThreadGuard.BindCurrentThread();
            Reset();
            _directory = Path.Combine(Path.GetTempPath(), "nw-game-entry-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown] public void TearDown()
        {
            foreach (var binding in _bindings) binding.Dispose();
            _bindings.Clear(); Reset();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        BootContext Context() => new BootContext(new App(new NowFrameWork.Core.Logging.Logger()), new AssetService(new Provider()),
            new FileSettingsStore<BootPreferences>(_directory, "preferences", new JsonSettingsCodec<BootPreferences>(validate: BootPreferences.Validate)));

        GameEntry.Binding Bind(BootContext context, IUiService ui = null)
        {
            var binding = GameEntry.Bind(context, ui); _bindings.Add(binding); return binding;
        }

        [Test] public void Unbound_RejectsServiceAccess()
        {
            Assert.IsFalse(GameEntry.IsReady);
            StringAssert.Contains("尚未就绪", Assert.Throws<InvalidOperationException>(() => { var _ = GameEntry.Assets; }).Message);
            Assert.Throws<ArgumentNullException>(() => GameEntry.Bind(null));
        }

        [Test] public void Binding_PublishesTheExistingBootServices()
        {
            var context = Context(); var ui = new UiService(new Factory(), Array.Empty<UiDefinition>());
            Bind(context, ui);
            Assert.IsTrue(GameEntry.IsReady); Assert.IsTrue(GameEntry.HasUI);
            Assert.AreSame(context.Assets, GameEntry.Assets); Assert.AreSame(ui, GameEntry.UI);
            Assert.AreSame(context.Configs, GameEntry.Configs); Assert.AreSame(context.Settings, GameEntry.Settings);
            Assert.AreSame(context.App.Log, GameEntry.Log); Assert.AreSame(context.App.Events, GameEntry.Events);
            Assert.AreSame(context.App.Clock, GameEntry.Clock); Assert.AreSame(context.App.Timers, GameEntry.Timers);
        }

        [Test] public void OptionalModules_AreExplicitlyUnavailable()
        {
            Bind(Context());
            Assert.IsFalse(GameEntry.HasUI); Assert.IsFalse(GameEntry.HasScenes);
            StringAssert.Contains("未启用 UI", Assert.Throws<InvalidOperationException>(() => { var _ = GameEntry.UI; }).Message);
            StringAssert.Contains("未启用场景", Assert.Throws<InvalidOperationException>(() => { var _ = GameEntry.Scenes; }).Message);
        }

        [Test] public void DuplicateBinding_DoesNotReplaceTheOriginal()
        {
            var context = Context(); Bind(context);
            Assert.Throws<InvalidOperationException>(() => GameEntry.Bind(Context()));
            Assert.AreSame(context.Assets, GameEntry.Assets);
        }

        [Test] public void Stopping_BlocksAccessAndRebinding_UntilOwnerCompletesCleanup()
        {
            var context = Context(); var owner = Bind(context);
            owner.BeginStopping(); owner.BeginStopping();
            Assert.IsFalse(GameEntry.IsReady);
            StringAssert.Contains("正在停止", Assert.Throws<InvalidOperationException>(() => { var _ = GameEntry.Assets; }).Message);
            Assert.Throws<InvalidOperationException>(() => GameEntry.Bind(context));
            owner.Dispose(); Bind(context); Assert.IsTrue(GameEntry.IsReady);
        }

        [Test] public void OldOwner_CannotStopOrClearANewerBinding()
        {
            var old = Bind(Context()); old.Dispose();
            var next = Context(); Bind(next);
            old.BeginStopping(); old.Dispose();
            Assert.IsTrue(GameEntry.IsReady); Assert.AreSame(next.Assets, GameEntry.Assets);
        }

        [Test] public void SubsystemReset_ClearsStatics_AndOldOwnerCannotClearNewPlaySession()
        {
            var old = Bind(Context()); Reset();
            Assert.IsFalse(GameEntry.IsReady);
            var next = Context(); Bind(next); old.BeginStopping(); old.Dispose();
            Assert.AreSame(next.Assets, GameEntry.Assets);
        }

        [Test] public void Boot_WaitsForProjectReadiness_ThenPublishesAndUnbindsOnStop()
        {
            var entry = new Entry { Gate = new UniTaskCompletionSource() };
            var session = new BootSession(Context, new BootOptions(), entry);
            var start = session.StartAsync();
            Assert.IsFalse(GameEntry.IsReady);
            entry.Gate.TrySetResult(); Complete(start);
            Assert.AreSame(session.Context.Assets, GameEntry.Assets);
            Complete(session.StopAsync()); Assert.IsFalse(GameEntry.IsReady); Assert.IsNull(session.Context);
        }

        [Test] public void Boot_BusinessFailureAfterBinding_RollsBackAndAllowsRetry()
        {
            var entry = new Entry { FailAfterBind = true };
            var session = new BootSession(Context, new BootOptions(), entry);
            Assert.Throws<InvalidOperationException>(() => Complete(session.StartAsync()));
            Assert.IsFalse(GameEntry.IsReady); Assert.IsTrue(session.CanStart);
            entry.FailAfterBind = false; Complete(session.StartAsync());
            Assert.IsTrue(GameEntry.IsReady); Complete(session.StopAsync());
        }

        [Test] public void Boot_CanceledBeforeProjectReadiness_DoesNotPublish()
        {
            var entry = new Entry { Gate = new UniTaskCompletionSource() };
            var session = new BootSession(Context, new BootOptions(), entry);
            using (var cancellation = new CancellationTokenSource())
            {
                var start = session.StartAsync(cancellation.Token);
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => Complete(start));
                Assert.IsFalse(GameEntry.IsReady); Assert.IsNull(session.Context); Assert.IsTrue(session.CanStart);
            }
        }

        [Test] public void Boot_CleanupFailure_RetainsStoppedBindingUntilRetry()
        {
            var entry = new Entry(); var session = new BootSession(Context, new BootOptions(), entry);
            Complete(session.StartAsync()); entry.FailCleanup = true;
            Assert.Throws<InvalidOperationException>(() => Complete(session.StopAsync()));
            Assert.IsFalse(GameEntry.IsReady);
            Assert.Throws<InvalidOperationException>(() => GameEntry.Bind(Context()));
            entry.FailCleanup = false; Complete(session.StopAsync());
            var fresh = Context(); Bind(fresh); Assert.AreSame(fresh.Assets, GameEntry.Assets);
        }

        [Test] public void AssetDemo_LoadFailure_UsesBootCleanupToUnbind()
        {
            var go = new GameObject("Asset Demo");
            try
            {
                var entry = go.AddComponent<AssetLoadDemo>();
                var session = new BootSession(Context, new BootOptions(), entry);
                Assert.Throws<InvalidOperationException>(() => Complete(session.StartAsync()));
                Assert.IsFalse(GameEntry.IsReady); Assert.IsNull(session.Context); Assert.IsTrue(session.CanStart);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
