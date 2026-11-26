using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Boot;
using NowFrameWork.Core.Fsm;
using NowFrameWork.Core.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.FrameworkTemplate
{
    /// <summary>
    /// 封装模块页的演示操作。通过 BootContext 借用服务，并持有这些操作创建的租约、池和附加场景。
    /// </summary>
    internal sealed class TemplateResourceDemo
    {
        enum Phase { Idle, Running }
        enum Step { First, Second }
        readonly struct SampleEvent { }

        // 空状态便于观察状态机的事件、层级和历史恢复，无需附带玩法代码。
        sealed class DemoState : IState
        {
            public void Enter() { }
            public void Tick() { }
            public void FixedTick() { }
            public void Exit() { }
        }

        readonly BootContext _context;
        readonly Transform _parent;
        readonly StateMachine<Phase> _flow = new StateMachine<Phase>();
        readonly StateMachine<Step> _steps = new StateMachine<Step>();
        readonly List<LeaseInfo> _leases = new List<LeaseInfo>();
        AssetLease<GameObject> _prefab;
        AssetLease<TextAsset> _text;
        AssetPool<GameObject> _pool;
        GameObject _instance;
        SceneLease _scene;

        public TemplateResourceDemo(BootContext context, Transform parent)
        {
            _context = context;
            _parent = parent;
            // 先配置子状态机，再把它作为父机的 Running 状态注册。退出再进入会恢复上次的子状态。
            _steps.EntryMode = StateEntryMode.LastState;
            _steps.AddState(Step.First, new DemoState());
            _steps.AddState(Step.Second, new DemoState());
            _steps.SetInitialState(Step.First);
            _steps.AddEventTransition(Step.First, "Advance", Step.Second);
            _flow.AddState(Phase.Idle, new DemoState());
            _flow.AddState(Phase.Running, _steps);
            _flow.SetInitialState(Phase.Idle);
            _flow.AddEventTransition(Phase.Idle, "Toggle", Phase.Running);
            _flow.AddEventTransition(Phase.Running, "Toggle", Phase.Idle);
        }

        public void Start() => _flow.Enter();
        // 宿主只驱动根状态机，活动中的子机由父机更新。
        public void Tick() => _flow.Tick();
        public void FixedTick() => _flow.FixedTick();

        public async UniTask<string> ReadConfigAsync(CancellationToken ct)
        {
            // 先演示一次直接读取。取出独立字符串后即可归还短租约。
            ReleaseText();
            _text = await _context.Assets.AcquireAsync<TextAsset>(TemplateContent.Config, ct);
            string direct = _text.Asset.text.Trim();
            ReleaseText();
            // 再通过配置服务读同一地址。缓存租约归配置服务持有，随 Boot 退出回收。
            TextAsset cached = await _context.Configs.LoadAsync<TextAsset>(TemplateContent.Config, ct);
            return $"Direct lease returned: {direct}\nConfig cache: {cached.text.Trim()}";
        }

        public async UniTask<string> TogglePooledInstanceAsync(CancellationToken ct)
        {
            if (_pool == null)
            {
                ReleasePrefab();
                _prefab = await _context.Assets.AcquireAsync<GameObject>(TemplateContent.Prefab, ct);
                // 建池前先拿到 Prefab，作为实例工厂的输入。池在创建时取得自己的保活租约。
                GameObject template = _prefab.Asset;
                _pool = await AssetPool<GameObject>.CreateAsync(_context.Assets, AssetKey.From(TemplateContent.Prefab),
                    () => Object.Instantiate(template, _parent), item => Object.Destroy(item),
                    _context.App.Clock, _context.App.Log, maxSize: 1, ct: ct);
            }
            // 交接完成后归还直接租约。此时池的保活租约保证工厂还能继续实例化 Prefab。
            ReleasePrefab();
            if (_instance != null)
            {
                // 归池前先隐藏对象。再次租用时重新激活，状态栏可用实例 ID 确认复用。
                _instance.SetActive(false);
                _pool.Release(_instance);
                _instance = null;
                return $"Prefab returned. Idle instances: {_pool.InactiveCount}";
            }
            _instance = _pool.Rent();
            _instance.name = "Pooled Cube";
            _instance.SetActive(true);
            return $"Prefab rented. Instance ID: {_instance.GetInstanceID()}";
        }

        /// <summary>首次点击附加加载，第二次点击通过同一 SceneLease 卸载。</summary>
        public async UniTask<string> ToggleSceneAsync(CancellationToken ct)
        {
            if (_scene == null)
            {
                _scene = await _context.Scenes.LoadAdditiveAsync(TemplateContent.Scene, ct);
                return "Additive scene loaded: FrameworkTemplateFixture";
            }
            await _scene.UnloadAsync();
            _scene = null;
            return "Additive scene unloaded.";
        }

        public async UniTask<string> RunCoreAsync(CancellationToken ct)
        {
            bool received = false;
            // EventBus 在本次调用中同步分发。作用域结束后立即退订。
            using (_context.App.Events.Subscribe<SampleEvent>((in SampleEvent _) => received = true))
                _context.App.Events.Publish(new SampleEvent());

            var elapsed = new UniTaskCompletionSource();
            double start = _context.App.Clock.UnscaledNow;
            // 用非缩放时钟，暂停游戏时间时仍会触发。取消等待后也要移除计时器。
            var timer = _context.App.Timers.DelayUnscaled(0.25, () => elapsed.TrySetResult());
            try { await elapsed.Task.AttachExternalCancellation(ct); }
            finally { _context.App.Timers.Cancel(timer); }
            return $"Event received: {received}. Timer: {_context.App.Clock.UnscaledNow - start:F2} s.";
        }

        public string AdvanceFsm()
        {
            if (_flow.CurrentStateId == Phase.Running && _steps.CurrentStateId == Step.First)
                _steps.SendEvent("Advance");
            else
                _flow.SendEvent("Toggle");
            return _flow.CurrentStateId == Phase.Running
                ? $"HFSM: Running / {_steps.CurrentStateId}" : "HFSM: Idle";
        }

        public string Diagnostics()
        {
            _leases.Clear();
            _context.Assets.DumpLeases(_leases);
            foreach (LeaseInfo lease in _leases)
                _context.App.Log.Write(LogLevel.Info, "Template", lease.ToString());
            _context.App.Log.Write(LogLevel.Info, "Template",
                $"App: {_context.App.Lifecycle.State}, system language: {_context.App.Platform.SystemLanguage}, " +
                $"audio paused: {_context.Audio.IsPaused}, persistent path: {_context.App.Platform.PersistentDataPath}");
            return $"Leases: {_context.Assets.ActiveLeases}, pending: {_context.Assets.PendingRequests}, configs: {_context.Configs.Count}. Details in Console.";
        }

        /// <summary>卸载场景并等待池实例销毁。某一步失败时保留相应字段供后续清理重试。</summary>
        public async UniTask DisposeAsync()
        {
            _flow.Exit();
            if (_scene != null)
            {
                await _scene.UnloadAsync();
                _scene = null;
            }
            if (_pool != null)
            {
                // AssetPool 先销毁实例，确认销毁后再归还池的保活租约。
                await _pool.DisposeAsync();
                _pool = null;
                _instance = null;
            }
            ReleasePrefab();
            ReleaseText();
        }

        void ReleasePrefab()
        {
            _prefab?.Dispose();
            _prefab = null;
        }

        void ReleaseText()
        {
            _text?.Dispose();
            _text = null;
        }
    }
}
