using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Audio;
using NowFrameWork.Boot;
using NowFrameWork.Core.Fsm;
using NowFrameWork.Core.Logging;
using NowFrameWork.Input;
using NowFrameWork.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>
    /// 框架漫游演示：一个入口按顺序走完公共能力，屏幕列出每步结果。
    /// 包含真实 Prefab 实例化与 UI 面板流程（依赖于场景里配置好的 UiRoot 与 EventSystem）。
    /// </summary>
    public sealed class FrameworkTourSample : MonoBehaviour, IBootEntry
    {
        struct TourPing { public int Value; }

        sealed class PhaseState : IState
        {
            public void Enter() { }
            public void Tick() { }
            public void FixedTick() { }
            public void Exit() { }
        }

        enum Phase { Ready, Active }

        static readonly UiKey<UiWindowSampleArgs> Home = new UiKey<UiWindowSampleArgs>(1, "Home");
        static readonly UiKey<UiWindowSampleArgs> Settings = new UiKey<UiWindowSampleArgs>(2, "Settings");

        [SerializeField] UiRoot _uiRoot;
        [SerializeField] EventSystem _eventSystem;
        [SerializeField] string _windowAddress = "UiWindows/Window";

        readonly List<string> _lines = new List<string>(24);
        GameEntry.Binding _binding;
        UiModule _ui;
        SceneLease _scene;
        AssetLease<GameObject> _prefabLease;
        GameObject _prefabInstance;
        Transform _prefabRoot;

        /// <summary>场景生成菜单调用，注入 UI 根预制体与事件系统。</summary>
        public void Configure(UiRoot root, EventSystem events)
        {
            _uiRoot = root;
            _eventSystem = events;
        }

        public async UniTask EnterAsync(BootContext context, CancellationToken ct)
        {
            _lines.Clear();

            await Step("日志", c => LogStep(context, c), ct);
            await Step("设置", c => SettingsStep(context, c), ct);
            await Step("配置", c => ConfigStep(context, c), ct);
            await Step("Prefab 实例", c => PrefabStep(context, c), ct);
            await Step("附加场景", c => SceneStep(context, c), ct);
            await Step("对象池", c => PoolStep(context, c), ct);
            await Step("事件", c => EventStep(context, c), ct);
            await Step("计时器", c => TimerStep(context, c), ct);
            await Step("时钟", c => ClockStep(context, c), ct);
            await Step("层级状态机", c => FsmStep(c), ct);
            await Step("音频", c => AudioStep(context, c), ct);
            await Step("输入门控", c => InputStep(c), ct);

            bool uiReady = await Step("UI 模块", c => UiModuleStep(context, c), ct);

            // UI 模块就绪后再绑定，业务即可通过 GameEntry.UI 访问
            _binding = GameEntry.Bind(context, uiReady ? _ui.Service : null, null);

            await Step("UI 流程", c => UiFlowStep(c), ct);
            await Step("取消", c => CancelStep(c), ct);

            _lines.Add("本地化见 UILocalization；Windows 发布见 BootIntegration。");
        }

        public async UniTask ExitAsync()
        {
            if (_ui != null)
            {
                await _ui.ShutdownAsync(CancellationToken.None);
                _ui = null;
            }

            if (_prefabInstance != null)
            {
                UnityEngine.Object.Destroy(_prefabInstance);
                await UnityObjectLifecycle.WaitDestroyedAsync(_prefabInstance);
                _prefabInstance = null;
            }

            _prefabLease?.Dispose();
            _prefabLease = null;

            if (_scene != null) { await _scene.UnloadAsync(); _scene = null; }

            _binding?.BeginStopping();
            _binding?.Dispose();
            _binding = null;
        }

        // ---------------- Core / Assets ----------------

        static UniTask LogStep(BootContext context, CancellationToken ct)
        {
            context.App.Log.Write(LogLevel.Info, "TOUR", "日志模块：分级与 Sink 已接通");
            return UniTask.CompletedTask;
        }

        static UniTask SettingsStep(BootContext context, CancellationToken ct)
        {
            context.Settings.Value.Language = "zh-CN";
            context.Settings.Save();
            return UniTask.CompletedTask;
        }

        static async UniTask ConfigStep(BootContext context, CancellationToken ct)
        {
            var config = await context.Configs.LoadAsync<TextAsset>("PCProbe/Config", ct);
            if (config == null || config.text.Length == 0) throw new InvalidOperationException("配置为空");
        }

        async UniTask PrefabStep(BootContext context, CancellationToken ct)
        {
            _prefabLease = await context.Assets.AcquireAsync<GameObject>("PCProbe/Prefab", ct);

            _prefabInstance = UnityEngine.Object.Instantiate(_prefabLease.Asset);
            _prefabInstance.name = "Tour Prefab Instance";
            _prefabInstance.transform.position = new Vector3(0f, 0.5f, 0f);
            _prefabRoot = _prefabInstance.transform;
        }

        async UniTask SceneStep(BootContext context, CancellationToken ct)
        {
            _scene = await context.Scenes.LoadAdditiveAsync("PCProbe/Scene", ct);
            await _scene.UnloadAsync();
            _scene = null;
        }

        static UniTask PoolStep(BootContext context, CancellationToken ct)
        {
            var pool = context.App.Pools.GetOrCreate("tour.builder", () => new StringBuilder());
            var item = pool.Get();
            try { item.Append("demo"); }
            finally { item.Clear(); pool.Release(item); }
            context.App.Pools.DisposePool("tour.builder");
            return UniTask.CompletedTask;
        }

        static UniTask EventStep(BootContext context, CancellationToken ct)
        {
            int seen = 0;
            using (context.App.Events.Subscribe<TourPing>((in TourPing e) => seen += e.Value))
                context.App.Events.Publish(new TourPing { Value = 1 });

            if (seen != 1) throw new InvalidOperationException("事件未按预期派发");
            return UniTask.CompletedTask;
        }

        static async UniTask TimerStep(BootContext context, CancellationToken ct)
        {
            bool fired = false;
            context.App.Timers.Delay(0.2, () => fired = true);
            await UniTask.WaitUntil(() => fired, cancellationToken: ct);
        }

        static UniTask ClockStep(BootContext context, CancellationToken ct)
        {
            if (context.App.Clock.Now < 0 || context.App.Clock.UnscaledNow < 0)
                throw new InvalidOperationException("时钟返回负值");
            return UniTask.CompletedTask;
        }

        static UniTask FsmStep(CancellationToken ct)
        {
            var fsm = new StateMachine<Phase>();
            fsm.AddState(Phase.Ready, new PhaseState());
            fsm.AddState(Phase.Active, new PhaseState());
            fsm.SetInitialState(Phase.Ready);
            fsm.AddEventTransition(Phase.Ready, "Begin", Phase.Active);
            fsm.Enter();
            fsm.SendEvent("Begin");

            if (!Equals(fsm.CurrentStateId, Phase.Active)) throw new InvalidOperationException("状态未切换");

            fsm.Exit();
            return UniTask.CompletedTask;
        }

        static async UniTask AudioStep(BootContext context, CancellationToken ct)
        {
            AudioModule audio = context.Audio;
            if (audio == null) throw new InvalidOperationException("音频模块未注册");

            bool played = await audio.PlayEffectAsync("audio/sfx", ct);
            if (!played) throw new InvalidOperationException("音效未播放（通道满或未启用）");
        }

        static UniTask InputStep(CancellationToken ct)
        {
            var input = new ActionInput(Array.Empty<IActionSource>());
            using (input.Suppress("tour.demo"))
                if (!input.IsSuppressed) throw new InvalidOperationException("门控未生效");

            if (input.IsSuppressed) throw new InvalidOperationException("门控未解除");
            return UniTask.CompletedTask;
        }

        // ---------------- UI ----------------

        async UniTask UiModuleStep(BootContext context, CancellationToken ct)
        {
            if (_uiRoot == null) throw new InvalidOperationException("场景未配置 UiRoot（请用生成菜单重建场景）");
            if (_eventSystem == null) throw new InvalidOperationException("场景未配置 EventSystem");

            _ui = new UiModule(context.Assets, _uiRoot, new[]
            {
                new UiDefinition(1, "Home", AssetKey.From(_windowAddress)),
                new UiDefinition(2, "Settings", AssetKey.From(_windowAddress))
                { Layer = UiLayer.Popup, Modal = true, CloseOnBackdrop = true }
            }, new CanvasGroupUiTransition(), _eventSystem, log: context.App.Log);

            await _ui.InitializeAsync(ct);
            await _ui.StartAsync(ct);
        }

        async UniTask UiFlowStep(CancellationToken ct)
        {
            if (_ui == null) throw new InvalidOperationException("UI 模块未就绪");

            IUiService ui = _ui.Service;

            // 1. 页面入栈
            await ui.Navigation.PushAsync(Home, new UiWindowSampleArgs
            {
                Title = "TOUR HOME\nPrefab + UI flow",
                Fullscreen = true,
                Color = new Color(0.05f, 0.12f, 0.23f),
            }, ct);

            if (ui.Navigation.PageCount != 1) throw new InvalidOperationException("页面未入栈");

            // 2. 打开模态弹窗
            var opened = await ui.OpenAsync(Settings, new UiWindowSampleArgs
            {
                Title = "TOUR SETTINGS\nModal popup",
                Color = new Color(0.12f, 0.22f, 0.38f),
            }, ct);

            if (!opened.IsOpen || !ui.TopModal.IsValid) throw new InvalidOperationException("模态弹窗未打开");

            // 3. 返回键关闭弹窗
            if (!await ui.RequestBackAsync(ct)) throw new InvalidOperationException("返回未处理");
            if (ui.TopModal.IsValid) throw new InvalidOperationException("弹窗未关闭");

            // 4. 页面隐藏 / 恢复
            UiHandle page = ui.Navigation.CurrentPage;
            await ui.HideAsync(page, ct);
            await ui.ShowAsync(page, ct);

            // 5. 关闭页面
            await ui.CloseAsync(page, ct);
            if (ui.Navigation.PageCount != 0) throw new InvalidOperationException("页面未关闭");
        }

        static async UniTask CancelStep(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.Cancel();

            try
            {
                await UniTask.WaitUntil(() => false, cancellationToken: cts.Token);
                throw new InvalidOperationException("取消未生效");
            }
            catch (OperationCanceledException)
            {
                // 预期路径
            }
        }

        async UniTask<bool> Step(string name, Func<CancellationToken, UniTask> body, CancellationToken ct)
        {
            try
            {
                await body(ct);
                _lines.Add("OK   " + name);
                return true;
            }
            catch (Exception error)
            {
                _lines.Add("FAIL " + name + "：" + error.Message);
                Debug.LogWarning("[Tour] " + name + " 失败: " + error);
                return false;
            }
        }

        void Update()
        {
            if (_prefabRoot != null) _prefabRoot.Rotate(0f, 20f * Time.deltaTime, 0f, Space.World);
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 760f, Mathf.Min(Screen.height - 24f, 560f)), GUI.skin.box);
            GUILayout.Label("框架漫游演示（真实 Prefab + UI 流程）");

            foreach (string line in _lines) GUILayout.Label(line);

            GUILayout.EndArea();
        }
    }
}
