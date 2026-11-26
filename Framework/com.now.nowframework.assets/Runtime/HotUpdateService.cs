using NowFrameWork.Core.Threading;
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

    /// <summary>准备完成后的资源计划。DownloadBytes 基于已经激活的 Catalog 和当前缓存计算。</summary>
    public readonly struct HotUpdatePlan
    {
        /// <summary>描述一次更新检查的结果。</summary>
        public HotUpdatePlan(bool hasUpdate, long downloadBytes)
        {
            HasUpdate = hasUpdate;
            DownloadBytes = downloadBytes;
        }
        /// <summary>是否有新 Catalog。</summary>
        public bool HasUpdate { get; }
        /// <summary>需要下载的字节数。</summary>
        public long DownloadBytes { get; }
        /// <summary>便于日志输出的摘要。</summary>
        public override string ToString() => "Catalog更新=" + HasUpdate + "，待下载=" + DownloadBytes + " bytes";
    }

    /// <summary>
    /// 仅主线程。启动安全点流程：PrepareAsync（检查、更新 Catalog、查询大小）→ DownloadAsync → 加载业务资源。
    /// PrepareAsync 会激活新目录，必须在业务资源/DLL 加载之前调用。
    /// 不提供下载后原子切换，也不替换已加载的 DLL；补丁代码通过重启加载。
    /// </summary>
    public sealed class HotUpdateService
    {
        readonly IAssetUpdateProvider _provider;
        readonly ILog _log;
        HotUpdateState _state;
        object _preparedKey;

        /// <summary>创建热更服务；提供者负责远端查询与下载。</summary>
        public HotUpdateService(IAssetUpdateProvider provider, ILog log = null)
        {
            MainThreadGuard.Assert("HotUpdateService.HotUpdateService");
            _provider = Guard.NotNull(provider, nameof(provider));
            _log = log ?? Logs.Null;
        }

        /// <summary>当前热更阶段。</summary>
        public HotUpdateState State
        {
            get
            {
                MainThreadGuard.Assert("HotUpdateService.State");
                return _state;
            }
        }
        /// <summary>是否正在检查或下载。</summary>
        public bool IsBusy
        {
            get
            {
                MainThreadGuard.Assert("HotUpdateService.IsBusy");
                return _state != HotUpdateState.Idle;
            }
        }

        /// <summary>只检查，不更新 Catalog，不提供可能基于旧目录的下载大小。</summary>
        public async UniTask<bool> CheckAsync(CancellationToken ct = default)
        {
            MainThreadGuard.Assert("HotUpdateService.CheckAsync");
            EnsureIdle();
            ct.ThrowIfCancellationRequested();
            _state = HotUpdateState.Checking;
            try { return await _provider.CheckForUpdatesAsync(ct); }
            finally { _state = HotUpdateState.Idle; }
        }

        /// <summary>准备启动所需资源，返回是否仍需下载。</summary>
        public async UniTask<HotUpdatePlan> PrepareAsync(object key, CancellationToken ct = default)
        {
            MainThreadGuard.Assert("HotUpdateService.PrepareAsync");
            EnsureIdle();
            if (key == null) throw new ArgumentNullException(nameof(key));
            ct.ThrowIfCancellationRequested();
            _preparedKey = null;
            _state = HotUpdateState.Checking;
            try
            {
                bool changed = await _provider.CheckForUpdatesAsync(ct);
                if (changed)
                {
                    _state = HotUpdateState.Applying;
                    await _provider.UpdateCatalogsAsync(ct);
                }
                ct.ThrowIfCancellationRequested();
                long bytes = await _provider.GetDownloadSizeAsync(key, ct);
                ct.ThrowIfCancellationRequested();
                _preparedKey = key;
                return new HotUpdatePlan(changed, bytes);
            }
            finally { _state = HotUpdateState.Idle; }
        }

        /// <summary>下载资源并报告进度。</summary>
        public async UniTask DownloadAsync(object key, IProgress<float> progress = null, CancellationToken ct = default)
        {
            MainThreadGuard.Assert("HotUpdateService.DownloadAsync");
            EnsureIdle();
            ct.ThrowIfCancellationRequested();
            if (_preparedKey == null || !Equals(_preparedKey, key))
                throw new InvalidOperationException("下载前须对同一个资源键调用 PrepareAsync");
            _state = HotUpdateState.Downloading;
            try
            {
                await _provider.DownloadAsync(key, progress, ct);
                if (_log.IsEnabled(LogLevel.Info))
                    _log.Write(LogLevel.Info, "HOTUPDATE", "资源下载完成；现在可以加载业务资源或热更 DLL");
            }
            finally { _state = HotUpdateState.Idle; }
        }

        void EnsureIdle()
        {
            if (IsBusy) throw new InvalidOperationException("热更流程正在进行中: " + _state);
        }
    }
}
