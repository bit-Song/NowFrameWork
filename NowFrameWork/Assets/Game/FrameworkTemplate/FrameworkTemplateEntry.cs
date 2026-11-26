using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Localization;
using NowFrameWork.Boot;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Storage;
using NowFrameWork.Input;
using NowFrameWork.Input.InputSystem;
using NowFrameWork.UI;
using NowFrameWork.Unity.Storage;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.FrameworkTemplate
{
    /// <summary>
    /// 由场景中的 BootFlow 调用。Boot 提供基础服务，这里装配 UI、输入和演示逻辑，
    /// 并在退出时清理本入口创建的模块、资源和异步操作。
    /// </summary>
    public sealed class FrameworkTemplateEntry : MonoBehaviour, IBootEntry
    {
        [Serializable]
        sealed class Preferences
        {
            public float Volume = 0.3f;
            public bool AlternateKey;
        }

        // ActionInput 使用整数 ID。CreateActions 会把这个 ID 映射到 Input System 的 Demo/Pulse。
        const int DemoAction = 1;
        [SerializeField] UiRoot _root;
        [SerializeField] EventSystem _eventSystem;
        [SerializeField] InputSystemDeviceWatcher _devices;

        BootContext _context;
        UiModule _ui;
        InputModule _back;
        InputActionAsset _actionAsset;
        InputAction _pulse;
        ActionInput _actions;
        GameEntry.Binding _binding;
        TemplateResourceDemo _resources;
        LanguageSelection _languages;
        FileSettingsStore<Preferences> _preferences;
        CancellationTokenSource _lifetime;
        UniTaskCompletionSource _command;
        bool _ready;
        bool _music;
        int _pulses;
        string _result;
        string _language;

        /// <summary>场景生成器写入这些引用，搭建其他场景时也可以直接在 Inspector 赋值。</summary>
        public void Configure(UiRoot root, EventSystem eventSystem, InputSystemDeviceWatcher devices)
        {
            _root = root;
            _eventSystem = eventSystem;
            _devices = devices;
        }

        /// <summary>Boot 基础模块就绪后调用。依次恢复设置、启动项目模块、绑定服务入口并打开首页。</summary>
        public async UniTask EnterAsync(BootContext context, CancellationToken ct)
        {
            _context = context;
            // ct 约束这次启动，_lifetime 约束启动后由按钮触发的操作。
            _lifetime = new CancellationTokenSource();
            _music = false;
            _pulses = 0;
            _result = "Choose an example. Use the arrow keys or D-pad to move, Enter / A to confirm, Esc / B to go back.";
            string folder = Path.Combine(context.App.Platform.PersistentDataPath, "FrameworkTemplate");
            _preferences = new FileSettingsStore<Preferences>(folder, "demo", new JsonSettingsCodec<Preferences>());
            _preferences.Load(() => new Preferences());

            // 示例独立保存语言和演示设置，语言表仍沿用项目里的 Unity Localization 资源。
            var languageStore = new FileSettingsStore<BootPreferences>(folder, "language",
                new JsonSettingsCodec<BootPreferences>(validate: BootPreferences.Validate));
            SettingsLoadResult languageLoad = languageStore.Load(() => new BootPreferences());
            _languages = new LanguageSelection(languageStore);
            await _languages.InitializeAsync(languageLoad.Source, ct);
            _language = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale.Identifier.Code;

            _ui = new UiModule(context.Assets, _root, TemplateContent.Definitions(), new CanvasGroupUiTransition(),
                _eventSystem, transform, context.App.Log, cacheOptions: new UiCacheOptions(capacity: 1));
            await _ui.InitializeAsync(ct);
            await _ui.StartAsync(ct);

            // 模块和动作源可用之后再向项目代码公开 GameEntry。
            CreateActions();
            _binding = GameEntry.Bind(context, _ui.Service, _actions);
            _back = new InputModule(RouteBackAsync, lifecycle: context.App.Lifecycle, log: context.App.Log,
                sources: new IBackSource[] { new InputSystemBackSource() });
            await _back.InitializeAsync(ct);
            await _back.StartAsync(ct);
            _resources = new TemplateResourceDemo(context, transform);
            _resources.Start();
            ApplyVolume(_preferences.Value.Volume);
            await _ui.Service.Navigation.PushAsync(TemplateContent.HomeKey, Panel(OpenModulesAsync, OpenSettingsAsync, ToastAsync), ct);
            _ready = true;
        }

        // 原生 EventSystem 读取 UI 移动和确认。这里的 Action Map 单独演示普通动作和改键。
        void CreateActions()
        {
            _actionAsset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("Demo");
            _pulse = map.AddAction("Pulse", InputActionType.Button, "<Keyboard>/p");
            _pulse.AddBinding("<Gamepad>/buttonWest");
            map.AddAction("Other", InputActionType.Button, "<Keyboard>/i"); // 为冲突检查提供一个已占用键位。
            _actionAsset.AddActionMap(map);
            if (_preferences.Value.AlternateKey) _pulse.ApplyBindingOverride(0, "<Keyboard>/o");
            _actionAsset.Enable();
            _actions = new ActionInput(new InputActionsSource(_actionAsset, (DemoAction, "Demo/Pulse")));
        }

        void Update()
        {
            if (!_ready) return;
            _back.Tick(Time.deltaTime, Time.unscaledDeltaTime);
            // 模态窗口打开时屏蔽普通动作，UI 导航继续由 EventSystem 驱动。
            _actions.SetGate("template.modal", _ui.Service.TopModal.IsValid);
            if (_actions.WasPressed(DemoAction)) _pulses++;
            _resources.Tick();
        }

        void FixedUpdate()
        {
            if (_ready) _resources.FixedTick();
        }

        // 命令顺序与 Prefab 的 _buttons 数组一致。入口统一串行执行，避免重复点击造成并发切页。
        TemplatePanel.Data Panel(params Func<CancellationToken, UniTask>[] commands)
            => new TemplatePanel.Data((index, visible) =>
            {
                // 可见期令牌用于判断能否开始操作。切页会隐藏原面板，后续操作由入口令牌管理。
                if (_ready && !visible.IsCancellationRequested && _command == null)
                    ExecuteAsync(commands[index]).Forget();
            }, Status);

        async UniTask ExecuteAsync(Func<CancellationToken, UniTask> action)
        {
            // 退出时先发出取消信号，再等待这个完成信号。finally 保证取消或异常也能结束等待。
            var done = new UniTaskCompletionSource();
            _command = done;
            try { await action(_lifetime.Token); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                _result = error.Message;
                _context.App.Log.Write(LogLevel.Error, "Template", error.ToString());
            }
            finally { _command = null; done.TrySetResult(); }
        }

        // 子页面入栈，返回时由导航服务恢复首页。弹窗则单独打开，不增加页面栈长度。
        async UniTask OpenModulesAsync(CancellationToken ct)
            => await GameEntry.UI.Navigation.PushAsync(TemplateContent.ModulesKey, Panel(
                token => ReportAsync(_resources.ReadConfigAsync(token)),
                token => ReportAsync(_resources.TogglePooledInstanceAsync(token)),
                token => ReportAsync(_resources.ToggleSceneAsync(token)),
                token => ReportAsync(_resources.RunCoreAsync(token)),
                _ => Report(_resources.AdvanceFsm()),
                _ => Report(_resources.Diagnostics()), HidePageAsync, TrimAsync, BackAsync), ct);

        async UniTask OpenSettingsAsync(CancellationToken ct)
            => await GameEntry.UI.OpenAsync(TemplateContent.SettingsKey, Panel(
                token => SetLanguageAsync(LanguageSelection.English, token),
                token => SetLanguageAsync(LanguageSelection.Chinese, token),
                ToggleMusicAsync, PlayEffectAsync, _ => ToggleVolume(), _ => ChangeKey(),
                _ => Report(ConflictingKey()), BackAsync), ct);

        async UniTask ToastAsync(CancellationToken ct)
        {
            var result = await GameEntry.UI.OpenAsync(TemplateContent.ToastKey, "Toast layer · focus stays on the page", ct);
            if (!result.IsOpen) return;
            try { await UniTask.Delay(TimeSpan.FromSeconds(1.2), ignoreTimeScale: true, cancellationToken: ct); }
            // 即使停留被取消，也要关闭已打开的会话。退出时使用本入口持有的 UI 服务完成清理。
            finally { await _ui.Service.CloseAsync(result.Handle); }
        }

        async UniTask HidePageAsync(CancellationToken ct)
        {
            // 显隐使用同一个句柄，页面会话和 Prefab 实例会保留。
            UiHandle page = GameEntry.UI.Navigation.CurrentPage;
            await GameEntry.UI.HideAsync(page, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(1), ignoreTimeScale: true, cancellationToken: ct);
            await GameEntry.UI.ShowAsync(page, ct);
            _result = "The existing page session was hidden and shown.";
        }

        async UniTask TrimAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            int before = _ui.Service.CachedCount;
            // 使用与 Unity 低内存通知相同的回收入口，方便在编辑器验证空闲缓存。
            await _ui.TrimForLowMemoryAsync();
            _result = $"UI cache trimmed: {before} → {_ui.Service.CachedCount}. Open pages stay alive.";
        }

        // 演示操作进行中消费物理返回键，空闲时由 UI 按弹窗、页面的顺序处理。
        UniTask<bool> RouteBackAsync(CancellationToken ct)
            => _command != null ? UniTask.FromResult(true) : _ui.Service.RequestBackAsync(ct);
        async UniTask BackAsync(CancellationToken ct) => await GameEntry.UI.RequestBackAsync(ct);
        async UniTask ReportAsync(UniTask<string> operation) => _result = await operation;
        UniTask Report(string value) { _result = value; return UniTask.CompletedTask; }

        async UniTask SetLanguageAsync(string code, CancellationToken ct)
        {
            await _languages.SwitchAsync(code, ct);
            _language = code;
            _result = "Language saved. The sample string above follows Unity Localization.";
        }

        async UniTask ToggleMusicAsync(CancellationToken ct)
        {
            if (_music) { GameEntry.Audio.StopMusic(); _music = false; }
            else _music = await GameEntry.Audio.PlayMusicAsync(TemplateContent.Music, ct);
            _result = _music ? "Looping BGM started." : "BGM stopped.";
        }

        // await 等待播放请求完成。第一条音效可以仍在播放，因此两次请求能演示并发。
        async UniTask PlayEffectAsync(CancellationToken ct)
        {
            bool first = await GameEntry.Audio.PlayEffectAsync(TemplateContent.Effect, ct);
            bool second = await GameEntry.Audio.PlayEffectAsync(TemplateContent.Effect, ct);
            _result = $"Two SFX requests: {first}, {second}. The audio module shares the clip lease.";
        }

        void ApplyVolume(float volume) => GameEntry.Audio.SetVolumes(volume, volume);

        UniTask ToggleVolume()
        {
            Preferences preferences = _preferences.Value;
            float previous = preferences.Volume;
            preferences.Volume = previous > 0f ? 0f : 0.3f;
            // 先保存，再应用运行状态。写入失败时恢复内存值，让界面和当前音量保持一致。
            try { _preferences.Save(); }
            catch { preferences.Volume = previous; throw; }
            ApplyVolume(preferences.Volume);
            return Report($"Volume saved: {preferences.Volume:P0}.");
        }

        UniTask ChangeKey()
        {
            Preferences preferences = _preferences.Value;
            bool previous = preferences.AlternateKey;
            preferences.AlternateKey = !previous;
            try { _preferences.Save(); }
            catch { preferences.AlternateKey = previous; throw; }
            // 仅覆盖 Pulse 的第一个绑定（键盘），后面的手柄绑定继续有效。
            if (preferences.AlternateKey) _pulse.ApplyBindingOverride(0, "<Keyboard>/o");
            else _pulse.RemoveBindingOverride(0);
            return Report("Demo action binding saved. Close this popup and try the new key.");
        }

        string ConflictingKey()
            => InputBindingConflicts.TryFind(_pulse, 0, "<Keyboard>/i", null, out var other, out _)
                ? $"Rejected I: already bound to {other.name} in Demo." : "I is available.";

        string Status()
        {
            string key = _preferences.Value.AlternateKey ? "O" : "P";
            string gate = _actions.IsSuppressed ? " · blocked by modal" : "";
            return $"{_result}\n\nDevice: {_devices.Tracker.Current}   |   Pulse: {key} / X × {_pulses}{gate}" +
                $"\nPages: {_ui.Service.Navigation.PageCount}   |   UI cache: {_ui.Service.CachedCount}" +
                $"\nLanguage: {_language}   |   Volume: {_preferences.Value.Volume:P0}";
        }

        /// <summary>Boot 调用。先关闭访问入口并等待当前命令退出，再释放本入口持有的对象。</summary>
        public async UniTask ExitAsync()
        {
            if (_lifetime == null) return;
            _ready = false;
            _binding?.BeginStopping();
            _lifetime.Cancel();
            if (_command != null) await _command.Task;
            if (_back != null)
            {
                await _back.ShutdownAsync(CancellationToken.None);
                _back = null;
            }
            // 只有清理成功才清空对应字段。停止失败时，下一次 Stop 可以从保留的对象继续清理。
            if (_resources != null)
            {
                await _resources.DisposeAsync();
                _resources = null;
            }
            if (_ui != null)
            {
                await _ui.DisposeAsync();
                _ui = null;
            }
            if (_actionAsset != null)
            {
                _actionAsset.Disable();
                Destroy(_actionAsset);
                _actionAsset = null;
            }
            _binding?.Dispose();
            _binding = null;
            _lifetime.Dispose();
            _lifetime = null;
            _context = null; // 归还借用的上下文引用；基础服务由 Boot 继续收尾。
        }
    }
}
