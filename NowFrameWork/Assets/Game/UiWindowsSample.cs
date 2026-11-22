using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Boot;
using NowFrameWork.Core.Async;
using NowFrameWork.Input;
using NowFrameWork.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>页面历史 + 两层模态窗口。入口拥有模块与异步操作，停止时先等待操作退出。</summary>
    public sealed class UiWindowsSample : MonoBehaviour, IBootEntry
    {
        [SerializeField] UiRoot _rootPrefab;
        [SerializeField] EventSystem _eventSystem;
        [SerializeField] string _homePrefab = "UiWindows/Window";
        [SerializeField] string _inventoryPrefab = "UiWindows/Window";
        [SerializeField] string _settingsPrefab = "UiWindows/Window";
        [SerializeField] string _confirmPrefab = "UiWindows/Window";
        [SerializeField] bool _cacheSettings;
        static readonly UiKey<UiWindowSampleArgs> Home = new UiKey<UiWindowSampleArgs>(1, "Home");
        static readonly UiKey<UiWindowSampleArgs> Inventory = new UiKey<UiWindowSampleArgs>(2, "Inventory");
        static readonly UiKey<UiWindowSampleArgs> Settings = new UiKey<UiWindowSampleArgs>(3, "Settings");
        static readonly UiKey<UiWindowSampleArgs> Confirm = new UiKey<UiWindowSampleArgs>(4, "Confirm");
        UiModule _module;
        InputModule _input;
        DoubleBackQuitFallback _quitFallback;
        GameEntry.Binding _binding;
        CancellationTokenSource _lifetime;
        TaskRunner _tasks;
        UniTaskCompletionSource _active;
        bool _entered;
        int _settingsOpens;
        public UiService Service => _module?.Service;
        public UiRoot Root => _module?.Root;
        public void Configure(UiRoot root, EventSystem events) { _rootPrefab = root; _eventSystem = events; }
        public void ConfigurePrefabAddresses(string home, string inventory, string settings, string confirm)
        { _homePrefab = home; _inventoryPrefab = inventory; _settingsPrefab = settings; _confirmPrefab = confirm; }

        public async UniTask EnterAsync(BootContext context, CancellationToken ct)
        {
            if (_module != null) throw new InvalidOperationException("窗口样例尚未退出。");
            _settingsOpens = 0;
            _lifetime = new CancellationTokenSource(); _tasks = new TaskRunner(context.App.Log);
            _module = new UiModule(context.Assets, _rootPrefab, new[]
            {
                new UiDefinition(1, "Home", AssetKey.From(_homePrefab)), new UiDefinition(2, "Inventory", AssetKey.From(_inventoryPrefab)),
                new UiDefinition(3, "Settings", AssetKey.From(_settingsPrefab))
                { Layer = UiLayer.Popup, Modal = true, CloseOnBackdrop = true, CacheOnClose = _cacheSettings },
                new UiDefinition(4, "Confirm", AssetKey.From(_confirmPrefab)) { Layer = UiLayer.Popup, Modal = true }
            }, new CanvasGroupUiTransition(), _eventSystem, log: context.App.Log,
                cacheOptions: new UiCacheOptions(capacity: 1, idleTimeout: TimeSpan.FromSeconds(15)));
            await _module.InitializeAsync(ct); await _module.StartAsync(ct); ct.ThrowIfCancellationRequested();
            _binding = GameEntry.Bind(context, Service);

            // 返回导航：屏上按钮走 Run(BackAsync)，物理返回键走 InputModule（防连发、后台不补发）
            _quitFallback = new DoubleBackQuitFallback(2f);
            _input = new InputModule(Service.RequestBackAsync, fallback: _quitFallback,
                lifecycle: context.App.Lifecycle, log: context.App.Log,
                sources: new IBackSource[] { new InputSystemBackSource() });
            await _input.InitializeAsync(ct);
            await _input.StartAsync(ct);
            await Service.Navigation.PushAsync(Home, HomeArgs(), ct);
            _entered = true;
        }
        UiWindowSampleArgs HomeArgs() => new UiWindowSampleArgs
        {
            Title = "HOME\nPage navigation / shared modal backdrop", Fullscreen = true, Color = new Color(0.05f, 0.12f, 0.23f),
            Next = () => Run(OpenInventoryAsync), Settings = () => Run(OpenSettingsAsync), Back = () => Run(BackAsync)
        };
        public async UniTask OpenInventoryAsync(CancellationToken ct)
            => await Service.Navigation.PushAsync(Inventory, new UiWindowSampleArgs
            {
                Title = "INVENTORY\nBack restores the same Home page", Fullscreen = true, Color = new Color(0.06f, 0.22f, 0.20f),
                Settings = () => Run(OpenSettingsAsync), Back = () => Run(BackAsync)
            }, ct);
        public async UniTask OpenSettingsAsync(CancellationToken ct)
            => await GameEntry.UI.OpenAsync(Settings, new UiWindowSampleArgs
            {
                Title = _cacheSettings ? "SETTINGS · OPEN " + ++_settingsOpens + "\nBack to cache this panel" : "SETTINGS\nClick backdrop or Back to close",
                Color = new Color(0.12f, 0.22f, 0.38f),
                Confirm = () => Run(OpenConfirmAsync), Back = () => Run(BackAsync)
            }, ct);
        public async UniTask OpenConfirmAsync(CancellationToken ct)
            => await GameEntry.UI.OpenAsync(Confirm, new UiWindowSampleArgs
            {
                Title = "CONFIRM\nBackdrop is blocked; use Back", Color = new Color(0.34f, 0.16f, 0.20f),
                Back = () => Run(BackAsync)
            }, ct);
        public async UniTask BackAsync(CancellationToken ct) => await Service.Navigation.BackAsync(ct);

        public async UniTask VerifyFlowAsync(CancellationToken ct)
        {
            while (Service.TopPopup.IsValid) await BackAsync(ct);
            while (Service.Navigation.PageCount > 1) await Service.Navigation.PopAsync(ct);
            UiHandle home = Service.Navigation.CurrentPage;
            for (int i = 0; i < 10; i++)
            {
                await OpenInventoryAsync(ct); await OpenSettingsAsync(ct); await OpenConfirmAsync(ct);
                if (!Root.BackdropVisible || Service.Navigation.PageCount != 2) throw new InvalidOperationException("窗口样例状态错误");
                await BackAsync(ct); await BackAsync(ct); await BackAsync(ct);
                if (Root.BackdropVisible || Service.Navigation.CurrentPage != home || Service.SessionCount != 1)
                    throw new InvalidOperationException("返回后存在窗口或历史残留");
            }
            Debug.Log("[UI WINDOWS] 10/10 页面/双弹窗/返回完成，保留根页 1。");
        }
        void Run(Func<CancellationToken, UniTask> action)
        {
            if (!_entered || _active != null) return;
            _tasks.Run(RunAsync(action), "UI 多窗口样例");
        }
        async UniTask RunAsync(Func<CancellationToken, UniTask> action)
        {
            var done = new UniTaskCompletionSource(); _active = done;
            try { await action(_lifetime.Token); }
            finally { _active = null; done.TrySetResult(); }
        }
        void Update()
        {
            _input?.Tick(Time.deltaTime, Time.unscaledDeltaTime);
        }
        void OnGUI()
        {
            if (!_entered) return;
            GUI.enabled = _active == null;
            float y = Screen.height - 50;
            if (GUI.Button(new Rect(10, y, 130, 36), "Inventory")) Run(OpenInventoryAsync);
            if (GUI.Button(new Rect(150, y, 130, 36), "Settings")) Run(OpenSettingsAsync);
            if (GUI.Button(new Rect(290, y, 130, 36), "Confirm")) Run(OpenConfirmAsync);
            if (GUI.Button(new Rect(430, y, 130, 36), "Back / Esc")) Run(BackAsync);
            if (GUI.Button(new Rect(570, y, 160, 36), "10 cycles")) Run(VerifyFlowAsync);
            if (_cacheSettings && GUI.Button(new Rect(740, y, 150, 36), "Trim cache (" + Service.CachedCount + ")"))
                Run(ct => Service.TrimCacheAsync(ct));
            GUI.enabled = true;

            if (_quitFallback != null && _quitFallback.IsPrompting)
            {
                GUI.Label(new Rect(0, y - 40, Screen.width, 30),
                    "再按一次返回键退出 (" + _quitFallback.RemainingSeconds.ToString("F1") + "s)");
            }
        }
        public async UniTask ExitAsync()
        {
            _entered = false; _binding?.BeginStopping(); _lifetime?.Cancel();
            if (_active != null) await _active.Task;

            if (_input != null)
            {
                await _input.ShutdownAsync(CancellationToken.None);
                _input = null;
            }

            if (_module != null) await _module.DisposeAsync();
            _binding?.Dispose(); _binding = null; _module = null;
            _tasks?.Dispose(); _tasks = null; _lifetime?.Dispose(); _lifetime = null;
        }
    }
}
