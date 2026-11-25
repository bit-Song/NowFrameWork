using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Audio;
using NowFrameWork.Boot;
using NowFrameWork.Core.Fsm;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Storage;
using NowFrameWork.Input;
using NowFrameWork.Unity.Storage;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 框架漫游演示：一个入口按顺序走完全部公共能力，屏幕列出每一步结果。
    /// 只做演示，不写业务；UI 多窗口与 Windows 发布见各自样例。
    /// </summary>
    public sealed class FrameworkTourSample : MonoBehaviour, IBootEntry
    {
        struct TourPing { public int Value; }

        sealed class PhaseState : IState
        {
            public string Name;
            public void Enter() { }
            public void Tick() { }
            public void FixedTick() { }
            public void Exit() { }
        }

        enum Phase { Ready, Active }

        readonly List<string> _lines = new List<string>(24);
        GameEntry.Binding _binding;
        SceneLease _scene;

        public async UniTask EnterAsync(BootContext context, CancellationToken ct)
        {
            _binding = GameEntry.Bind(context);
            _lines.Clear();

            await Step("日志", c => LogStep(context, c), ct);
            await Step("设置", c => SettingsStep(context, c), ct);
            await Step("配置", c => ConfigStep(context, c), ct);
            await Step("资源租约", c => LeaseStep(context, c), ct);
            await Step("附加场景", c => SceneStep(context, c), ct);
            await Step("对象池", c => PoolStep(context, c), ct);
            await Step("事件", c => EventStep(context, c), ct);
            await Step("计时器", c => TimerStep(context, c), ct);
            await Step("时钟", c => ClockStep(context, c), ct);
            await Step("层级状态机", c => FsmStep(c), ct);
            await Step("音频", c => AudioStep(context, c), ct);
            await Step("输入门控", c => InputStep(c), ct);
            await Step("取消", c => CancelStep(c), ct);

            _lines.Add("本地化见 UILocalization 样例；UI 多窗口见 UIWindows；Windows 发布见 BootIntegration。");
        }

        public async UniTask ExitAsync()
        {
            if (_scene != null) { await _scene.UnloadAsync(); _scene = null; }
            _binding?.BeginStopping();
            _binding?.Dispose();
            _binding = null;
        }

        // ---------------- 各能力演示 ----------------

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
            if (config == null || config.text.Length == 0) throw new InvalidDataException("配置为空");
        }

        static async UniTask LeaseStep(BootContext context, CancellationToken ct)
        {
            using var lease = await context.Assets.AcquireAsync<GameObject>("PCProbe/Prefab", ct);
            var instance = UnityEngine.Object.Instantiate(lease.Asset);
            UnityEngine.Object.Destroy(instance);

            if (!await UnityObjectLifecycle.WaitDestroyedAsync(instance))
                throw new TimeoutException("实例未在预期帧内销毁");
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
            fsm.AddState(Phase.Ready, new PhaseState { Name = "Ready" });
            fsm.AddState(Phase.Active, new PhaseState { Name = "Active" });
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

        async UniTask Step(string name, Func<CancellationToken, UniTask> body, CancellationToken ct)
        {
            try
            {
                await body(ct);
                _lines.Add("OK   " + name);
            }
            catch (Exception error)
            {
                _lines.Add("FAIL " + name + "：" + error.Message);
                Debug.LogWarning("[Tour] " + name + " 失败: " + error);
            }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 760f, Mathf.Min(Screen.height - 24f, 520f)), GUI.skin.box);
            GUILayout.Label("框架漫游演示（每步结果）");

            foreach (string line in _lines) GUILayout.Label(line);

            GUILayout.EndArea();
        }
    }
}
