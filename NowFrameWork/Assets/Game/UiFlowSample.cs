using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Boot;
using NowFrameWork.Core.Async;
using NowFrameWork.Core.Logging;
using NowFrameWork.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>项目 UI 接入样例。由 BootFlow 调用，不与基础 Hub 重复注册资源模块。</summary>
    public sealed class UiFlowSample : MonoBehaviour, IBootEntry
    {
        [SerializeField] UiRoot _rootPrefab;
        [SerializeField] EventSystem _eventSystem;
        [SerializeField] string _panelAddress = "UiProbe/Panel";
        static readonly UiKey<EmptyArgs> PanelKey = new UiKey<EmptyArgs>(1, "SamplePanel");
        BootContext _context;
        Scope _scope;
        UiModule _module;
        UiHandle _handle;
        CancellationTokenSource _lifetime;
        UniTaskCompletionSource _active;
        TaskRunner _tasks;
        bool _exiting;
        bool _entered;

        public UiService Service => _module?.Service;
        public void Configure(UiRoot root, EventSystem eventSystem) { _rootPrefab = root; _eventSystem = eventSystem; }

        public async UniTask EnterAsync(BootContext context, CancellationToken ct)
        {
            _context = context; _exiting = false; _entered = false;
            _lifetime = new CancellationTokenSource(); _tasks = new TaskRunner(context.App.Log);
            _module = new UiModule(context.Assets, _rootPrefab,
                new[] { new UiDefinition(PanelKey.Id, "SamplePanel", AssetKey.From(_panelAddress)) },
                new CanvasGroupUiTransition(), _eventSystem, log: context.App.Log);
            _scope = new Scope("Game UI", null, context.App.Clock, context.App.Log);
            _scope.Modules.Register(_module);
            await _scope.StartupAsync(ct);
            await OpenAsync(ct);
            _entered = true;
            Write("UI 就绪。底部按钮可测试显隐、快速反向和 100 次打开/关闭。");
        }

        public async UniTask OpenAsync(CancellationToken ct = default)
        {
            UiOpenResult result = await Service.OpenAsync(PanelKey, EmptyArgs.Default, ct);
            if (result.IsOpen) _handle = result.Handle;
        }

        public async UniTask Run100Async(CancellationToken ct = default)
        {
            await Service.CloseAsync(_handle, ct); _handle = default;
            int baseline = _context.Assets.ActiveLeases;
            for (int i = 0; i < 100; i++)
            {
                ct.ThrowIfCancellationRequested();
                await OpenAsync(ct);
                await Service.CloseAsync(_handle, ct); _handle = default;
            }
            if (_context.Assets.ActiveLeases != baseline || Service.SessionCount != 0)
                throw new InvalidOperationException("UI 100 次测试后存在残留租约或会话");
            Write("100/100 完成，SessionCount=0，ActiveLeases=" + _context.Assets.ActiveLeases);
        }

        async UniTask ReverseAsync(CancellationToken ct)
        {
            await Service.ShowAsync(_handle, ct);
            UniTask hide = Service.HideAsync(_handle, ct);
            await UniTask.NextFrame(cancellationToken: ct);
            UniTask show = Service.ShowAsync(_handle, ct);
            try { await hide; } catch (OperationCanceledException) { }
            await show;
            Write("快速反向完成，最终状态=" + Service.GetVisibilityState(_handle));
        }

        void Run(Func<CancellationToken, UniTask> action)
        {
            if (_active != null || _exiting || _tasks == null) return;
            _tasks.Run(RunActionAsync(action), "UI 样例操作");
        }

        async UniTask RunActionAsync(Func<CancellationToken, UniTask> action)
        {
            var finished = new UniTaskCompletionSource(); _active = finished;
            try { await action(_lifetime.Token); }
            finally { _active = null; finished.TrySetResult(); }
        }

        void OnGUI()
        {
            if (!_entered || _module == null || _exiting) return;
            float y = Screen.height - 60;
            GUI.enabled = _active == null;
            if (GUI.Button(new Rect(10, y, 100, 36), "Open")) Run(OpenAsync);
            if (GUI.Button(new Rect(120, y, 100, 36), "Hide")) Run(ct => Service.HideAsync(_handle, ct));
            if (GUI.Button(new Rect(230, y, 100, 36), "Show")) Run(ct => Service.ShowAsync(_handle, ct));
            if (GUI.Button(new Rect(340, y, 100, 36), "Close")) Run(ct => Service.CloseAsync(_handle, ct));
            if (GUI.Button(new Rect(450, y, 140, 36), "Reverse")) Run(ReverseAsync);
            if (GUI.Button(new Rect(600, y, 180, 36), "100 Open/Close")) Run(Run100Async);
            GUI.enabled = true;
        }

        public async UniTask ExitAsync()
        {
            if (_context == null) return;
            _exiting = true;
            _entered = false;
            _lifetime.Cancel();
            if (_active != null) await _active.Task;
            // 模块清理失败时保留引用，阻止 Boot 继续卸载资源依赖。
            if (_module != null) await _module.DisposeAsync();
            if (_scope != null) await _scope.DisposeAsync();
            _module = null; _scope = null;
            _tasks.Dispose(); _tasks = null;
            _lifetime.Dispose(); _lifetime = null;
            _context = null;
        }

        void Write(string message) => _context.App.Log.Write(LogLevel.Info, "UI", message);
    }
}
