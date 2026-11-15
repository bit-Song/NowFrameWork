using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Utility;

namespace NowFrameWork.Assets
{
    public enum HotUpdateState
    {
        Idle = 0,
        Checking = 1,
        Downloading = 2,
        Applying = 3,
    }

    /// <summary>一次热更检查的结果。</summary>
    public readonly struct HotUpdatePlan
    {
        public HotUpdatePlan(bool hasUpdate, long downloadBytes)
        {
            HasUpdate = hasUpdate;
            DownloadBytes = downloadBytes;
        }

        public bool HasUpdate { get; }

        /// <summary>需要下载的字节数；无更新时为 0。</summary>
        public long DownloadBytes { get; }

        public override string ToString()
            => HasUpdate ? "有更新 (" + (DownloadBytes / 1024f).ToString("F1") + " KB)" : "无更新";
    }

    /// <summary>
    /// 热更流程：检查 → 下载 → 应用。
    ///
    /// 三段分开是有意的：
    /// 1. **检查**可以静默做（启动时）；
    /// 2. **下载**要展示进度、允许取消、允许后台继续；
    /// 3. **应用**（切目录）会让已加载的资源失效，必须由业务决定时机（通常是回启动页/重启）。
    ///
    /// 状态机保证同一时刻只有一个阶段在跑；并发调用会抛 InvalidOperationException。
    ///
    /// 线程：仅主线程。
    /// </summary>
    public sealed class HotUpdateService
    {
        readonly IAssetUpdateProvider _provider;
        readonly ILog _log;

        HotUpdateState _state = HotUpdateState.Idle;

        public HotUpdateService(IAssetUpdateProvider provider, ILog log = null)
        {
            _provider = Guard.NotNull(provider, nameof(provider));
            _log = log ?? Logs.Null;
        }

        public HotUpdateState State => _state;

        public bool IsBusy => _state != HotUpdateState.Idle;

        /// <summary>检查是否有更新，并给出需要下载的大小。</summary>
        public async UniTask<HotUpdatePlan> CheckAsync(object catalogKey, CancellationToken ct = default)
        {
            EnsureIdle();
            _state = HotUpdateState.Checking;

            try
            {
                bool hasUpdate = await _provider.CheckForUpdatesAsync(ct);

                if (!hasUpdate) return new HotUpdatePlan(false, 0);

                long bytes = await _provider.GetDownloadSizeAsync(catalogKey, ct);

                if (_log.IsEnabled(LogLevel.Info))
                    _log.Write(LogLevel.Info, "HOTUPDATE", "发现更新，需下载 " + (bytes / 1024f).ToString("F1") + " KB");

                return new HotUpdatePlan(true, bytes);
            }
            finally
            {
                _state = HotUpdateState.Idle;
            }
        }

        /// <summary>下载依赖内容；可取消（取消后已下载部分由 Provider 缓存）。</summary>
        public async UniTask DownloadAsync(object catalogKey, IProgress<float> progress = null, CancellationToken ct = default)
        {
            EnsureIdle();
            _state = HotUpdateState.Downloading;

            try
            {
                await _provider.DownloadAsync(catalogKey, progress, ct);
            }
            finally
            {
                _state = HotUpdateState.Idle;
            }
        }

        /// <summary>
        /// 应用新目录（切换 catalog）。
        ///
        /// **调用后已加载的资源可能失效**，业务应先回到安全状态（如回启动页）。
        /// </summary>
        public async UniTask<int> ApplyAsync(CancellationToken ct = default)
        {
            EnsureIdle();
            _state = HotUpdateState.Applying;

            try
            {
                int updated = await _provider.UpdateCatalogsAsync(ct);

                if (_log.IsEnabled(LogLevel.Info))
                    _log.Write(LogLevel.Info, "HOTUPDATE", "已应用 " + updated + " 个目录更新");

                return updated;
            }
            finally
            {
                _state = HotUpdateState.Idle;
            }
        }

        void EnsureIdle()
        {
            if (_state != HotUpdateState.Idle)
                throw new InvalidOperationException("热更流程正在进行中: " + _state);
        }
    }
}