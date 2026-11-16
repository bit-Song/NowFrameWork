using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using NowFrameWork.Assets;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Storage;
using NowFrameWork.Core.Threading;
using NowFrameWork.Unity;
using NowFrameWork.Unity.Storage;
using UnityEngine;
using Logger = NowFrameWork.Core.Logging.Logger;

namespace NowFrameWork.Boot.Tests
{
    public class BootSessionTests
    {
        sealed class Provider : IAssetProvider, IAssetProviderLifecycle, IAssetUpdateProvider
        {
            public readonly List<string> Calls = new List<string>();
            public TextAsset Asset;
            public int Released;
            public bool ResolveFails;
            public Func<CancellationToken, UniTask> Init;
            public Func<CancellationToken, UniTask> Download;
            public UniTaskCompletionSource<ProviderHandle> LoadGate;
            public UniTask InitializeAsync(CancellationToken ct) { Calls.Add("init"); return Init == null ? UniTask.CompletedTask : Init(ct); }
            public UniTask<string> ResolveCanonicalKeyAsync(AssetKey key, CancellationToken ct)
            {
                Calls.Add("resolve"); ct.ThrowIfCancellationRequested();
                if (ResolveFails) throw new InvalidOperationException("required config missing");
                return UniTask.FromResult(key.Address);
            }
            public UniTask<ProviderHandle> LoadAsync(string canonical, CancellationToken ct)
            { Calls.Add("load"); return LoadGate != null ? LoadGate.Task : UniTask.FromResult(new ProviderHandle(canonical, Asset)); }
            public void Release(ProviderHandle handle) { Released++; Calls.Add("release"); }
            public UniTask<bool> CheckForUpdatesAsync(CancellationToken ct) { Calls.Add("check"); return UniTask.FromResult(true); }
            public UniTask<int> UpdateCatalogsAsync(CancellationToken ct) { Calls.Add("catalog"); return UniTask.FromResult(1); }
            public UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct) { Calls.Add("size"); return UniTask.FromResult(10L); }
            public UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct)
            { Calls.Add("download"); return Download == null ? UniTask.CompletedTask : Download(ct); }
        }
        sealed class Entry : IBootEntry
        {
            public int Entered;
            public int Exited;
            public Func<BootContext, CancellationToken, UniTask> Enter;
            public Func<UniTask> Exit;
            public UniTask EnterAsync(BootContext context, CancellationToken ct) { Entered++; return Enter == null ? UniTask.CompletedTask : Enter(context, ct); }
            public UniTask ExitAsync() { Exited++; return Exit == null ? UniTask.CompletedTask : Exit(); }
        }
        sealed class Code : IBootCodeLoader
        {
            public int Calls;
            public bool Fail;
            public UniTask<Assembly> LoadAsync(IAssetService assets, CancellationToken ct)
            {
                Calls++;
                return Fail ? UniTask.FromException<Assembly>(new InvalidOperationException("bad dll")) : UniTask.FromResult(typeof(BootSession).Assembly);
            }
        }
        sealed class BrokenScenes : ISceneService
        {
            public UniTask<SceneLease> LoadAdditiveAsync(object key, CancellationToken ct) => UniTask.FromException<SceneLease>(new InvalidOperationException("scene failed"));
        }
        string _directory;
        TextAsset _asset;
        Provider _provider;
        Entry _entry;
        readonly List<BootContext> _contexts = new List<BootContext>();
        int _created;

        [SetUp] public void SetUp()
        {
            MainThreadGuard.BindCurrentThread();
            _directory = Path.Combine(Path.GetTempPath(), "nwf-boot-" + Guid.NewGuid().ToString("N"));
            _asset = new TextAsset("boot-config"); _provider = new Provider { Asset = _asset }; _entry = new Entry();
            _contexts.Clear(); _created = 0;
        }
        [TearDown] public void TearDown()
        {
            foreach (var context in _contexts)
            {
                context.Configs.DisposeAsync().GetAwaiter().GetResult();
                context.App.ShutdownAsync().GetAwaiter().GetResult();
            }
            UnityEngine.Object.DestroyImmediate(_asset);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
        FileSettingsStore<BootPreferences> Settings(int version = 1) => new FileSettingsStore<BootPreferences>(_directory, "prefs",
            new JsonSettingsCodec<BootPreferences>(version, validate: version == 1 ? BootPreferences.Validate : (Action<BootPreferences>)null));
        BootContext Create()
        {
            _created++;
            var context = new BootContext(new App(new Logger()), new AssetService(_provider), Settings(), new BrokenScenes());
            _contexts.Add(context); return context;
        }
        BootSession Session(BootOptions options = null, Code code = null, Func<UniTask> wait = null) =>
            new BootSession(Create, options ?? new BootOptions(configAddress: "config"), _entry, code, cleanupWait: wait);
        static void Complete(UniTask task) => task.GetAwaiter().GetResult();

        [Test] public void Success_RetainsConfigAndSettings_UntilExplicitStop()
        {
            var session = Session(); Complete(session.StartAsync()); var context = session.Context;
            Assert.AreEqual(BootState.Running, session.State); Assert.AreEqual(1f, session.Progress);
            Assert.AreEqual(1, context.Assets.ActiveLeases); Assert.AreSame(_asset, context.Configuration);
            Assert.AreEqual(SettingsLoadSource.Defaults, context.SettingsResult.Source); Assert.IsFalse(File.Exists(context.Settings.FilePath));
            Complete(session.StopAsync());
            Assert.AreEqual(1, _entry.Exited); Assert.AreEqual(1, _provider.Released);
            Assert.AreEqual(0, context.Assets.ActiveLeases); Assert.IsNull(session.Context); Assert.IsTrue(session.CanStart);
        }

        [Test] public void RequiredConfigFailure_IsNotSilentlyIgnored_AndRetryUsesFreshContext()
        {
            _provider.ResolveFails = true; var session = Session();
            Assert.Throws<InvalidOperationException>(() => Complete(session.StartAsync()));
            Assert.AreEqual("Config", session.FailureStage); Assert.AreEqual(0, _entry.Entered); Assert.IsTrue(session.CanStart);
            _provider.ResolveFails = false; Complete(session.StartAsync());
            Assert.AreEqual(2, _created); Assert.AreNotSame(_contexts[0], _contexts[1]);
            Complete(session.StopAsync());
        }

        [Test] public void PartialEntryFailure_ExitsEntryBeforeReleasingDependencies()
        {
            _entry.Enter = (_, __) => throw new InvalidOperationException("entry failed");
            _entry.Exit = () => { Assert.AreEqual(1, _contexts[0].Assets.ActiveLeases); return UniTask.CompletedTask; };
            var session = Session(); Assert.Throws<InvalidOperationException>(() => Complete(session.StartAsync()));
            Assert.AreEqual("Entry", session.FailureStage); Assert.AreEqual(1, _entry.Exited);
            Assert.AreEqual(0, _contexts[0].Assets.ActiveLeases); Assert.IsTrue(session.CanStart);
        }

        [Test] public void CleanupFailure_KeepsDependenciesAndBlocksStart_UntilStopRetry()
        {
            var session = Session(); Complete(session.StartAsync());
            _entry.Exit = () => throw new InvalidOperationException("still using resources");
            Assert.Throws<InvalidOperationException>(() => Complete(session.StopAsync()));
            Assert.AreEqual(BootState.CleanupFailed, session.State); Assert.IsFalse(session.CanStart);
            Assert.AreEqual(1, session.Context.Assets.ActiveLeases); Assert.AreEqual(0, _provider.Released);
            Assert.Throws<InvalidOperationException>(() => session.StartAsync());
            _entry.Exit = null; Complete(session.StopAsync());
            Assert.AreEqual(2, _entry.Exited); Assert.IsTrue(session.CanStart);
        }

        [Test] public void StartupAndCleanupFailure_PreserveBothErrors_ThenCanFinishCleanup()
        {
            _entry.Enter = (_, __) => throw new InvalidOperationException("enter error");
            _entry.Exit = () => throw new InvalidOperationException("exit error");
            var session = Session(); var error = Assert.Throws<AggregateException>(() => Complete(session.StartAsync()));
            Assert.AreEqual(2, error.InnerExceptions.Count); Assert.AreEqual("Entry", session.FailureStage);
            Assert.IsNotNull(session.Context); Assert.AreEqual(0, _provider.Released);
            _entry.Exit = null; Complete(session.StopAsync()); Assert.IsTrue(session.CanStart);
        }

        [Test] public void DuplicateStartWhileRunning_DoesNotCreateSecondOwner()
        {
            var session = Session(); Complete(session.StartAsync());
            Assert.Throws<InvalidOperationException>(() => session.StartAsync()); Assert.AreEqual(1, _created);
            Complete(session.StopAsync());
        }

        [Test] public void StopDuringStarting_CancelsAndWaitsForNonCooperativeInit()
        {
            var gate = new UniTaskCompletionSource(); _provider.Init = _ => gate.Task;
            var session = Session(); var start = session.StartAsync();
            Assert.Throws<InvalidOperationException>(() => session.StartAsync());
            var stop = session.StopAsync(); Assert.AreEqual(UniTaskStatus.Pending, stop.Status);
            Assert.IsFalse(session.CanStart); gate.TrySetResult();
            Assert.Catch<OperationCanceledException>(() => Complete(start)); Complete(stop);
            Assert.AreEqual(BootState.Stopped, session.State); Assert.AreEqual(0, _entry.Entered); Assert.IsNull(session.Context);
        }

        [Test] public void ConcurrentStop_WaitsForOneEntryExit()
        {
            var exit = new UniTaskCompletionSource(); _entry.Exit = () => exit.Task;
            var session = Session(); Complete(session.StartAsync()); var first = session.StopAsync(); var second = session.StopAsync();
            Assert.AreEqual(1, _entry.Exited); Assert.AreEqual(1, session.Context.Assets.ActiveLeases);
            exit.TrySetResult(); Complete(first); Complete(second); Assert.AreEqual(1, _provider.Released);
        }

        [Test] public void CanceledBeforeStart_DoesNotCreateContextOrReadSettings()
        {
            using (var source = new CancellationTokenSource())
            {
                source.Cancel(); var session = Session(); Assert.Catch<OperationCanceledException>(() => Complete(session.StartAsync(source.Token)));
                Assert.AreEqual(0, _created); Assert.IsTrue(session.CanStart); Assert.AreEqual(BootState.Canceled, session.State);
            }
        }

        [Test] public void ResourceUpdate_RunsBeforeAnyConfigLease()
        {
            var session = Session(new BootOptions("config", checkResourceUpdates: true, updateKey: "boot"));
            _provider.Download = _ => { Assert.AreEqual(0, _contexts[0].Assets.ActiveLeases); return UniTask.CompletedTask; };
            Complete(session.StartAsync());
            CollectionAssert.AreEqual(new[] { "init", "check", "catalog", "size", "download", "resolve", "load" }, _provider.Calls);
            Complete(session.StopAsync());
        }

        [Test] public void UpdateFailure_DoesNotEnterProjectOrLoadConfig()
        {
            _provider.Download = _ => throw new InvalidOperationException("offline");
            var session = Session(new BootOptions("config", checkResourceUpdates: true, updateKey: "boot"));
            Assert.Throws<InvalidOperationException>(() => Complete(session.StartAsync()));
            Assert.AreEqual("Resources", session.FailureStage); Assert.IsFalse(_provider.Calls.Contains("load")); Assert.IsTrue(session.CanStart);
        }

        [Test] public void DisabledOptions_DoNotCallUpdateOrCodePlugin()
        {
            var code = new Code(); var session = Session(code: code); Complete(session.StartAsync());
            Assert.AreEqual(0, code.Calls); Assert.IsFalse(_provider.Calls.Contains("check")); Complete(session.StopAsync());
        }

        [Test] public void AttemptedCodeLoad_RequiresProcessRestart_EvenAfterFailure()
        {
            var code = new Code { Fail = true }; var session = Session(new BootOptions("config", loadCode: true), code);
            Assert.Throws<InvalidOperationException>(() => Complete(session.StartAsync()));
            Assert.AreEqual("Code", session.FailureStage); Assert.IsTrue(session.RequiresRestart); Assert.IsFalse(session.CanStart);
            Assert.IsNull(session.Context); Assert.AreEqual(1, _provider.Released);
            Assert.Throws<InvalidOperationException>(() => session.StartAsync()); Assert.AreEqual(1, code.Calls);
        }

        [Test] public void CodeResult_IsHandedToEntry_AndCannotReloadAfterSuccessfulStop()
        {
            var code = new Code(); _entry.Enter = (context, _) => { Assert.AreEqual(typeof(BootSession).Assembly, context.CodeAssembly); return UniTask.CompletedTask; };
            var session = Session(new BootOptions(loadCode: true), code); Complete(session.StartAsync()); Complete(session.StopAsync());
            Assert.IsTrue(session.RequiresRestart); Assert.IsFalse(session.CanStart);
        }

        [Test] public void SettingsFutureSchema_BlocksResourcesAndPreservesBytes()
        {
            var future = Settings(2); future.Load(() => new BootPreferences { Schema = 2 }); future.Save();
            var bytes = File.ReadAllBytes(future.FilePath); var session = Session();
            Assert.Throws<SettingsCompatibilityException>(() => Complete(session.StartAsync()));
            Assert.AreEqual("Settings", session.FailureStage); Assert.AreEqual(0, _provider.Calls.Count);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(future.FilePath));
        }

        [Test] public void SettingsRecovery_ExposesBackupSourceWithoutAutomaticallyWriting()
        {
            var saved = Settings(); saved.Load(() => new BootPreferences()); saved.Save(); saved.Value.Language = "zh-CN"; saved.Save();
            File.WriteAllText(saved.FilePath, "broken"); var session = Session(new BootOptions()); Complete(session.StartAsync());
            Assert.AreEqual(SettingsLoadSource.Backup, session.Context.SettingsResult.Source);
            Assert.IsInstanceOf<InvalidDataException>(session.Context.SettingsResult.PrimaryError);
            Assert.AreEqual("broken", File.ReadAllText(saved.FilePath)); Complete(session.StopAsync());
        }

        [Test] public void SceneFailure_CleansConfigAndDoesNotEnterProject()
        {
            var session = Session(new BootOptions("config", "scene")); Assert.Throws<InvalidOperationException>(() => Complete(session.StartAsync()));
            Assert.AreEqual("Scene", session.FailureStage); Assert.AreEqual(0, _entry.Entered); Assert.AreEqual(1, _provider.Released);
        }

        [Test] public void CleanupWaitsForLateAssetResult_BeforePermittingRetry()
        {
            _provider.LoadGate = new UniTaskCompletionSource<ProviderHandle>(); var cleanupGate = new UniTaskCompletionSource();
            var session = Session(wait: () => cleanupGate.Task);
            using (var source = new CancellationTokenSource())
            {
                var start = session.StartAsync(source.Token); source.Cancel();
                Assert.AreEqual(UniTaskStatus.Pending, start.Status); Assert.IsFalse(session.CanStart);
                _provider.LoadGate.TrySetResult(new ProviderHandle("config", _asset)); cleanupGate.TrySetResult();
                Assert.Catch<OperationCanceledException>(() => Complete(start));
                Assert.AreEqual(1, _provider.Released); Assert.IsTrue(session.CanStart); Assert.IsNull(session.Context);
            }
        }

        [Test] public void RemainingPool_BlocksSceneAndConfigReleaseUntilOwnerRemovesIt()
        {
            var session = Session(); Complete(session.StartAsync()); var context = session.Context;
            context.App.Pools.GetOrCreate("project", () => new object()).Get();
            Assert.Throws<InvalidOperationException>(() => Complete(session.StopAsync()));
            Assert.AreEqual(1, context.Assets.ActiveLeases); Assert.AreEqual(0, _provider.Released);
            Assert.AreEqual(BootState.CleanupFailed, session.State);
            context.App.Pools.DisposePool("project", force: true); // 本用例是纯托管对象，没有 Unity 延迟销毁。
            Complete(session.StopAsync()); Assert.IsNull(session.Context); Assert.AreEqual(1, _provider.Released);
        }

        [Test] public void CleanupRetry_ReinvokesIdempotentEntrySoOwnerCanFinishItsPools()
        {
            var session = Session(); Complete(session.StartAsync()); var context = session.Context;
            context.App.Pools.GetOrCreate("project", () => new object());
            _entry.Exit = () =>
            {
                if (_entry.Exited > 1) context.App.Pools.DisposePool("project");
                return UniTask.CompletedTask;
            };
            Assert.Throws<InvalidOperationException>(() => Complete(session.StopAsync()));
            Assert.AreEqual(1, context.Assets.ActiveLeases);
            Complete(session.StopAsync()); Assert.AreEqual(2, _entry.Exited); Assert.IsTrue(session.CanStart);
        }

        [Test] public void RemainingExternalLease_BlocksNewAttemptUntilOwnerReleasesIt()
        {
            var session = Session(); Complete(session.StartAsync()); var context = session.Context;
            var ownedByEntry = context.Assets.AcquireAsync<TextAsset>("other", default).GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => Complete(session.StopAsync()));
            Assert.IsFalse(session.CanStart); Assert.AreEqual(1, context.Assets.ActiveLeases);
            ownedByEntry.Dispose(); Complete(session.StopAsync()); Assert.IsTrue(session.CanStart);
        }

        [Test] public void WorkerStart_IsRejectedBeforeFactory()
        {
            var session = Session(); Exception error = null;
            var thread = new Thread(() => { try { session.StartAsync(); } catch (Exception e) { error = e; } }) { IsBackground = true };
            thread.Start(); Assert.IsTrue(thread.Join(3000)); Assert.IsInstanceOf<InvalidOperationException>(error); Assert.AreEqual(0, _created);
        }

        [Test] public void InvalidOptionalConfiguration_FailsBeforeAnyFactoryInvocation()
        {
            Assert.Throws<ArgumentException>(() => new BootOptions(checkResourceUpdates: true));
            Assert.Throws<ArgumentException>(() => Session(new BootOptions(loadCode: true)));
            Assert.AreEqual(0, _created);
        }
    }
}
