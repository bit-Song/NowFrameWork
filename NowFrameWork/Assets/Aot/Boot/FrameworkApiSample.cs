using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Core.Async;
using NowFrameWork.Core.Logging;
using UnityEngine;

namespace NowFrameWork.Boot
{
    /// <summary>
    /// 仅主线程的项目接入示例。由 BootFlow 注入上下文，Play 后可从组件菜单运行。
    /// 不依赖 UI；实例/池/附加场景由本入口清理，配置由 BootContext 清理。
    /// </summary>
    public sealed class FrameworkApiSample : MonoBehaviour, IBootEntry
    {
        [SerializeField] string _prefabAddress = "PCProbe/Prefab";
        [SerializeField] string _configAddress = "PCProbe/Config";
        [SerializeField] string _sceneAddress = "PCProbe/Scene";
        [SerializeField] float _holdSeconds = 1;
        BootContext _context;
        CancellationTokenSource _lifetime;
        TaskRunner _tasks;
        UniTaskCompletionSource _running;
        AssetPool<GameObject> _pool;
        SceneLease _scene;
        bool _exiting;

        struct ProbeEvent { }

        /// <summary>借用启动上下文；自行申请的租约/订阅仍须由调用者释放。</summary>
        public BootContext Context => _context ?? throw new InvalidOperationException("请先启动 BootFlow");

        public UniTask EnterAsync(BootContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_context != null) throw new InvalidOperationException("示例入口尚未退出");
            _context = context;
            _lifetime = new CancellationTokenSource();
            _tasks = new TaskRunner(context.App.Log);
            _exiting = false;
            Write("入口已就绪。Play 后在本组件菜单选择 1.运行完整 API 流程；日志前缀 [API]。");
            return UniTask.CompletedTask;
        }

        /// <summary>测试事件、帧驱动计时器、配置、Prefab/资源池、附加场景及各自的释放。</summary>
        public async UniTask RunAllAsync(CancellationToken ct = default)
        {
            if (_context == null || _exiting) throw new InvalidOperationException("入口尚未就绪或正在退出");
            if (_running != null) throw new InvalidOperationException("API 流程正在运行");
            if (_pool != null || _scene != null) throw new InvalidOperationException("上次清理未完成，请停止 BootFlow 重试清理");
            var finished = new UniTaskCompletionSource();
            _running = finished;
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token))
            {
                try
                {
                    await TestCoreAsync(cancellation.Token);
                    TextAsset config = await Context.Configs.LoadAsync<TextAsset>(_configAddress, cancellation.Token);
                    Write("配置加载成功: " + config.text.Trim());
                    Write("设置读取成功: Language=" + Context.Settings.Value.Language);
                    int baseline = Context.Assets.ActiveLeases;
                    await TestPoolAsync(cancellation.Token);
                    if (Context.Assets.ActiveLeases != baseline)
                        throw new InvalidOperationException("资源池释放后租约未回到基线");
                    _scene = await Context.Scenes.LoadAdditiveAsync(_sceneAddress, cancellation.Token);
                    Write("附加场景加载成功: " + _scene.Key);
                    await UniTask.Delay(TimeSpan.FromSeconds(Math.Max(0, _holdSeconds)),
                        ignoreTimeScale: true, cancellationToken: cancellation.Token);
                    await CleanupOwnedAsync();
                    Write("完整流程通过。配置缓存仍由 Boot 持有；点击 Boot 的停止按钮后释放。");
                    PrintDiagnostics();
                }
                finally
                {
                    // 保留失败的资源字段供 Exit 重试；完成信号只表示操作已退出，不假装清理成功。
                    _running = null;
                    finished.TrySetResult();
                }
            }
        }

        async UniTask TestCoreAsync(CancellationToken ct)
        {
            bool received = false;
            using (Context.App.Events.Subscribe<ProbeEvent>((in ProbeEvent evt) => received = true))
                Context.App.Events.Publish(new ProbeEvent());
            if (!received) throw new InvalidOperationException("事件未收到");
            var elapsed = new UniTaskCompletionSource();
            var timer = Context.App.Timers.DelayUnscaled(0.1, () => elapsed.TrySetResult());
            try
            {
                // BootFlow 在 Running 时驱动 App.Tick；不在 Enter 内等待这个计时器。
                await elapsed.Task.AttachExternalCancellation(ct);
                Write("事件订阅/发布/退订、App.Tick 计时器通过");
            }
            finally { Context.App.Timers.Cancel(timer); }
        }

        async UniTask TestPoolAsync(CancellationToken ct)
        {
            using (AssetLease<GameObject> prefab = await Context.Assets.AcquireAsync<GameObject>(_prefabAddress, ct))
            {
                GameObject template = prefab.Asset;
                _pool = await AssetPool<GameObject>.CreateAsync(Context.Assets, AssetKey.From(_prefabAddress),
                    () => Instantiate(template), item => { if (item != null) Destroy(item); },
                    Context.App.Clock, Context.App.Log, maxSize: 4, ct: ct);
            }
            GameObject instance = _pool.Rent();
            instance.name = "ApiSample_PooledInstance";
            Write("Prefab 已加载并创建实例，可在 Hierarchy 查看 ApiSample_PooledInstance");
            await UniTask.Delay(TimeSpan.FromSeconds(Math.Max(0, _holdSeconds)),
                ignoreTimeScale: true, cancellationToken: ct);
            _pool.Release(instance);
            await _pool.DisposeAsync();
            _pool = null;
            Write("实例归池 → 池销毁 → 确认对象消失 → 租约归还完成");
        }

        /// <summary>显式保存到 BootSample/preferences；不自动改变 Unity 的语言设置。</summary>
        public void SaveLanguage(string language)
        {
            if (_exiting) throw new InvalidOperationException("入口正在退出");
            if (string.IsNullOrWhiteSpace(language)) throw new ArgumentException("语言不能为空", nameof(language));
            Context.Settings.Value.Language = language;
            Context.Settings.Save();
            Write("设置已显式保存: " + language);
        }

        [ContextMenu("2.输出资源诊断")]
        public void PrintDiagnostics()
        {
            var leases = new List<LeaseInfo>();
            Context.Assets.DumpLeases(leases);
            Write("Pending=" + Context.Assets.PendingRequests + ", ActiveLeases=" + Context.Assets.ActiveLeases
                + ", Configs=" + Context.Configs.Count + ", Language=" + Context.Settings.Value.Language);
            foreach (LeaseInfo lease in leases) Write(lease.ToString());
        }

        [ContextMenu("1.运行完整 API 流程")]
        void RunFromMenu()
        {
            if (!Application.isPlaying || _tasks == null)
            {
                Debug.LogWarning("[API] 请先 Play，并等待 Boot 启动完成");
                return;
            }
            _tasks.Run(RunAllAsync(), "API 完整流程");
        }

        [ContextMenu("3.保存测试语言 zh-CN")]
        void SaveFromMenu()
        {
            if (!Application.isPlaying || _context == null || _exiting) return;
            SaveLanguage("zh-CN");
        }

        public async UniTask ExitAsync()
        {
            if (_context == null) return;
            _exiting = true;
            Exception cancelError = null;
            try { _lifetime.Cancel(); } catch (Exception error) { cancelError = error; }
            if (_running != null) await _running.Task;
            await CleanupOwnedAsync();
            if (cancelError != null) throw cancelError;
            _tasks.Dispose();
            _tasks = null;
            _lifetime.Dispose();
            _lifetime = null;
            Write("项目入口收尾完成；Boot 将继续释放配置、检查租约归零并关闭 App");
            _context = null;
        }

        async UniTask CleanupOwnedAsync()
        {
            if (_pool != null) { await _pool.DisposeAsync(); _pool = null; }
            if (_scene != null) { await _scene.UnloadAsync(); _scene = null; }
        }

        void Write(string message) => Context.App.Log.Write(LogLevel.Info, "API", message);
    }
}
