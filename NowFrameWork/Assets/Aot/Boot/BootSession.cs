using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Threading;

namespace NowFrameWork.Boot
{
    /// <summary>
    /// 仅主线程的项目装配样例，不是通用任务图。失败清理完成后才能新建上下文重试。
    /// 成功时资源由 Context 保持；Stop 中入口退出失败会保留依赖，允许再次 Stop。
    /// </summary>
    public sealed class BootSession
    {
        readonly Func<BootContext> _factory;
        readonly BootOptions _options;
        readonly IBootEntry _entry;
        readonly IBootCodeLoader _codeLoader;
        readonly Func<UniTask> _cleanupWait;
        readonly double _cleanupSeconds;
        CancellationTokenSource _startupCancellation;
        UniTaskCompletionSource _startupFinished;
        UniTaskCompletionSource _stopFinished;
        bool _starting;
        bool _stopping;
        bool _entryAttempted;

        public BootState State { get; private set; }
        public string Stage { get; private set; } = "Idle";
        public float Progress { get; private set; }
        public string FailureStage { get; private set; }
        public Exception LastError { get; private set; }
        public BootContext Context { get; private set; }
        public bool RequiresRestart { get; private set; }
        public bool CanStart => !_starting && !_stopping && Context == null && !RequiresRestart;

        public BootSession(Func<BootContext> factory, BootOptions options, IBootEntry entry, IBootCodeLoader codeLoader = null,
            double cleanupSeconds = 10, Func<UniTask> cleanupWait = null)
        {
            MainThreadGuard.Assert("BootSession.ctor");
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
            if (options.LoadCode && codeLoader == null) throw new ArgumentException("启用代码热更必须提供 IBootCodeLoader");
            if (cleanupSeconds <= 0 || double.IsNaN(cleanupSeconds) || double.IsInfinity(cleanupSeconds))
                throw new ArgumentOutOfRangeException(nameof(cleanupSeconds));
            _codeLoader = codeLoader;
            _cleanupSeconds = cleanupSeconds;
            _cleanupWait = cleanupWait ?? NextFrame;
        }

        static async UniTask NextFrame() => await UniTask.Yield();

        public UniTask StartAsync(CancellationToken ct = default)
        {
            MainThreadGuard.Assert("BootSession.StartAsync");
            if (!CanStart) throw new InvalidOperationException(RequiresRestart ? "代码加载已进入进程，必须重启" : "启动/关闭中，或仍持有未清理上下文");
            _starting = true;
            _entryAttempted = false;
            _stopFinished = null;
            LastError = null; FailureStage = null; Progress = 0;
            State = BootState.Starting;
            var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _startupCancellation = source;
            var done = new UniTaskCompletionSource();
            _startupFinished = done;
            var result = new UniTaskCompletionSource();
            StartCoreAsync(source, done, result).Forget();
            return result.Task;
        }

        async UniTask StartCoreAsync(CancellationTokenSource source, UniTaskCompletionSource done, UniTaskCompletionSource result)
        {
            Exception failure = null;
            CancellationToken ct = source.Token;
            try
            {
                SetStage("Compose", 0); ct.ThrowIfCancellationRequested();
                Context = _factory() ?? throw new InvalidOperationException("Boot factory 返回 null");
                if (Context.Consumed) throw new InvalidOperationException("BootContext 不能重复使用");
                Context.Consumed = true;
                if (!string.IsNullOrEmpty(_options.SceneAddress) && Context.Scenes == null)
                    throw new InvalidOperationException("配置了场景却没有场景服务");
                SetStage("Settings", 0.1f);
                Context.SettingsResult = Context.Settings.Load(() => new BootPreferences { Language = Context.App.Platform.SystemLanguage });
                if (Context.SettingsResult.PrimaryError != null)
                    Context.App.Log.Write(LogLevel.Warn, "BOOT", "设置从备份恢复: " + Context.SettingsResult.PrimaryError.Message);
                ct.ThrowIfCancellationRequested();
                SetStage("Modules", 0.2f); await Context.App.StartupAsync(ct);
                SetStage("Resources", 0.35f);
                if (_options.CheckResourceUpdates)
                {
                    var update = new HotUpdateService(Context.Assets, Context.App.Log);
                    await update.PrepareAsync(_options.UpdateKey, ct);
                    await update.DownloadAsync(_options.UpdateKey, null, ct);
                }
                ct.ThrowIfCancellationRequested();
                SetStage("Config", 0.55f);
                if (!string.IsNullOrEmpty(_options.ConfigAddress))
                    Context.Configuration = await Context.Configs.LoadAsync<UnityEngine.Object>(_options.ConfigAddress, ct);
                ct.ThrowIfCancellationRequested();
                SetStage("Code", 0.7f);
                if (_options.LoadCode)
                {
                    RequiresRestart = true; // 元数据补充/Assembly.Load 均不可回滚，异常或取消也不能清除此标志。
                    Context.CodeAssembly = await _codeLoader.LoadAsync(Context.Assets, ct);
                    if (Context.CodeAssembly == null) throw new InvalidOperationException("代码加载器返回 null");
                }
                ct.ThrowIfCancellationRequested();
                SetStage("Scene", 0.8f);
                if (!string.IsNullOrEmpty(_options.SceneAddress))
                    Context.Scene = await Context.Scenes.LoadAdditiveAsync(_options.SceneAddress, ct)
                        ?? throw new InvalidOperationException("场景服务返回 null");
                ct.ThrowIfCancellationRequested();
                SetStage("Entry", 0.9f); _entryAttempted = true;
                await _entry.EnterAsync(Context, ct);
                ct.ThrowIfCancellationRequested();
                SetStage("Running", 1); State = BootState.Running;
            }
            catch (Exception error)
            {
                failure = error; LastError = error; FailureStage = Stage;
                try
                {
                    await CleanupAsync();
                    State = error is OperationCanceledException && ct.IsCancellationRequested ? BootState.Canceled : BootState.Failed;
                }
                catch (Exception cleanupError)
                {
                    failure = LastError = new AggregateException("Boot 失败且清理未完成", error, cleanupError);
                    State = BootState.CleanupFailed;
                }
            }
            finally
            {
                source.Dispose(); _startupCancellation = null; _starting = false;
                done.TrySetResult();
            }
            if (failure == null) result.TrySetResult();
            else if (failure is OperationCanceledException && ct.IsCancellationRequested) result.TrySetCanceled(ct);
            else result.TrySetException(failure);
        }

        public UniTask StopAsync()
        {
            MainThreadGuard.Assert("BootSession.StopAsync");
            if (_stopFinished != null) return _stopFinished.Task;
            _stopping = true;
            var completion = new UniTaskCompletionSource();
            _stopFinished = completion;
            StopCoreAsync(completion).Forget();
            return completion.Task;
        }

        async UniTask StopCoreAsync(UniTaskCompletionSource completion)
        {
            Exception failure = null;
            try
            {
                Exception cancelError = null;
                try { _startupCancellation?.Cancel(); } catch (Exception error) { cancelError = error; }
                if (_starting) await _startupFinished.Task;
                State = BootState.Stopping;
                await CleanupAsync();
                if (cancelError != null) throw cancelError;
                State = BootState.Stopped;
            }
            catch (Exception error)
            {
                failure = LastError = error;
                State = BootState.CleanupFailed;
                _stopFinished = null;
            }
            finally { _stopping = false; }
            if (failure == null) completion.TrySetResult(); else completion.TrySetException(failure);
        }

        async UniTask CleanupAsync()
        {
            if (Context == null) return;
            Stage = "Cleanup";
            if (_entryAttempted)
            {
                await _entry.ExitAsync(); // 入口尚未停止时，不继续卸载它仍可能使用的依赖。
                // 直到整段清理完成才清除此标志；后续依赖收尾失败时，允许幂等入口再次补做清理。
            }
            if (Context.App.Pools.PoolCount != 0)
                throw new InvalidOperationException("项目入口退出后仍有基础池；请完成移除和实例收尾，再重试停止");
            if (Context.Scene != null) { await Context.Scene.UnloadAsync(); Context.Scene = null; }
            Context.App.Pools.Dispose(); // 只关闭空注册表，不以强制销毁来证明项目实例已安全退出。
            await Context.Configs.DisposeAsync(); Context.Configuration = null;
            await Context.Audio.DisposeAsync(); // 音频持有自己的租约，先停止声音，再检查资源是否归零。
            var clock = Stopwatch.StartNew();
            while (Context.Assets.PendingRequests != 0)
            {
                if (clock.Elapsed.TotalSeconds >= _cleanupSeconds) throw new TimeoutException("Boot 等待在途资源收尾超时");
                await _cleanupWait();
            }
            if (Context.Assets.ActiveLeases != 0) throw new InvalidOperationException("Boot 关闭时仍有资源租约：" + Context.Assets.ActiveLeases);
            Context.Assets.RetryUnclaimedReleases();
            await Context.App.ShutdownAsync();
            Context = null;
            _entryAttempted = false;
        }

        void SetStage(string stage, float progress) { Stage = stage; Progress = progress; }
    }
}
