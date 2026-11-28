using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Assets.Adapter;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Storage;
using NowFrameWork.Unity;
using NowFrameWork.Unity.Storage;
using UnityEngine;

namespace NowFrameWork.Boot
{
    /// <summary>Addressables 装配样例。成功后保持运行，由 Stop/销毁负责收尾；不是 GameBootstrap 的派生类。</summary>
    public sealed class BootFlow : MonoBehaviour
    {
        [SerializeField] string _configAddress = "";
        [SerializeField] string _sceneAddress = "";
        [SerializeField] bool _runOnStart = true;
        [SerializeField] bool _checkResourceUpdates;
        [SerializeField] string _updateKey = "";
        [SerializeField] bool _loadCode;
        [SerializeField] MonoBehaviour _codeLoader;
        [SerializeField] MonoBehaviour _projectEntry;
        [SerializeField] NowFrameWork.UI.UiCatalog _uiCatalog;
        [SerializeField] WindowsResourceReleases _resourceReleases;
        readonly StringBuilder _messages = new StringBuilder();
        BootSession _session;
        AddressablesProvider _provider;
        ReleaseSelection _selectedRelease;
        CancellationTokenSource _selectingRelease;
        bool _releaseRestartRequired;
        bool _releaseTrialConfirmed;
        bool _runningAttempt;
        bool _destroyed;

        sealed class PlaceholderEntry : IBootEntry
        {
            public UniTask EnterAsync(BootContext context, System.Threading.CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                context.App.Log.Write(LogLevel.Info, "BOOT", "占位项目入口就绪；未启动游戏玩法");
                return UniTask.CompletedTask;
            }
            public UniTask ExitAsync() => UniTask.CompletedTask;
        }

        void Start() { if (_runOnStart) Run().Forget(); }

        /// <summary>当前会话上下文；未启动或已停止时为 null。</summary>
        public BootContext Context => _session?.Context;

        public async UniTask Run()
        {
            if (_destroyed || _runningAttempt) return;
            _runningAttempt = true;
            try
            {
                if (_releaseRestartRequired) throw new InvalidOperationException("发布集启动未完成，请重启后重新选版");
                if (_session == null)
                {
                    var entry = _projectEntry == null ? new PlaceholderEntry() : _projectEntry as IBootEntry;
                    if (entry == null) throw new ArgumentException("项目入口组件必须实现 IBootEntry");
                    var loader = _codeLoader as IBootCodeLoader;
                    var options = new BootOptions(_configAddress, _sceneAddress, _checkResourceUpdates, _updateKey, _loadCode);
                    if (_resourceReleases != null)
                    {
                        if (_checkResourceUpdates || _loadCode)
                            throw new InvalidOperationException("普通 AOT 发布模式要求关闭旧 Catalog 更新和代码加载");
                        using var selection = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                        _selectingRelease = selection;
                        try { _selectedRelease = await _resourceReleases.SelectAsync(selection.Token); }
                        finally { _selectingRelease = null; }
                        selection.Token.ThrowIfCancellationRequested();
                        Write(_selectedRelease == null ? "使用随包发布集" :
                            "选中发布集 " + _selectedRelease.Id + (_selectedRelease.IsTrial ? "（试用）" : "（已确认）"));
                    }
                    _session = new BootSession(CreateContext, options, entry, loader, uiCatalog: _uiCatalog);
                    if (transform.parent == null) DontDestroyOnLoad(gameObject);
                }
                await _session.StartAsync(destroyCancellationToken);
                if (_selectedRelease?.IsTrial == true && !_releaseTrialConfirmed)
                {
                    _resourceReleases.Running();
                    _releaseTrialConfirmed = true;
                }
                Write("启动完成；上下文保持到停止。设置可由项目入口显式 Save。");
            }
            catch (OperationCanceledException) { HandleStartFailure(); Write("启动已取消"); }
            catch (Exception error) { HandleStartFailure(); Write("启动失败 [" + _session?.FailureStage + "]: " + error.Message); }
            finally { _runningAttempt = false; }
        }

        void HandleStartFailure()
        {
            if (_selectedRelease != null && _session?.State != BootState.Running)
            {
                // 取消的目录操作可能在稍后完成。留到新进程再装配，避免两次启动共享旧操作。
                _releaseRestartRequired = true;
                if (_selectedRelease.IsTrial && !_releaseTrialConfirmed && _resourceReleases != null)
                    _resourceReleases.Failed();
            }
            if (_session?.Context == null) _provider?.RemoveReleaseCatalog();
        }

        BootContext CreateContext()
        {
            var app = App.Create();
            _provider = new AddressablesProvider(_selectedRelease, TimeSpan.FromSeconds(5));
            var assets = new AssetService(_provider, app.Log);
            var settings = new FileSettingsStore<BootPreferences>(Path.Combine(app.Platform.PersistentDataPath, "BootSample"),
                "preferences", new JsonSettingsCodec<BootPreferences>(validate: BootPreferences.Validate));
            return new BootContext(app, assets, settings, new AddressablesSceneService(_provider, TimeSpan.FromSeconds(10)));
        }

        public async UniTask Stop()
        {
            if (_selectingRelease != null)
            {
                _selectingRelease.Cancel();
                await UniTask.WaitUntil(() => _selectingRelease == null);
            }
            if (_session == null) return;
            try
            {
                await _session.StopAsync();
                _provider?.RemoveReleaseCatalog();
                Write("停止与清理完成");
            }
            catch (Exception error) { Write("清理未完成，可重试停止: " + error.Message); }
        }

        void Update()
        {
            if (_session?.State == BootState.Running) _session.Context.App.Tick(Time.deltaTime, Time.unscaledDeltaTime);
        }
        void LateUpdate() { if (_session?.State == BootState.Running) _session.Context.App.LateTick(); }
        void FixedUpdate() { if (_session?.State == BootState.Running) _session.Context.App.FixedTick(Time.fixedDeltaTime); }
        void OnApplicationPause(bool paused)
        {
            var lifecycle = _session?.Context?.App.Lifecycle;
            if (lifecycle == null) return;
            if (paused) lifecycle.NotifyPause(); else lifecycle.NotifyResume();
        }
        void OnApplicationFocus(bool focused) => OnApplicationPause(!focused);
        void OnApplicationQuit() => _session?.Context?.App.Lifecycle.NotifyQuit();
        void OnDestroy()
        {
            _destroyed = true;
            // 不在 MonoBehaviour 销毁后访问它或入口组件；项目应先 await Stop，再销毁 Boot 根对象。
            if (_session != null) Stop().Forget(error => Debug.LogException(error));
        }

        void Write(string text) { _messages.AppendLine(text); Debug.Log("[Boot] " + text); }
        void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 1100, 80), _session == null ? "Boot 未装配" :
                _session.State + " / " + _session.Stage + " / " + (_session.Progress * 100).ToString("F0") + "%");
            if (_session == null || _session.CanStart)
            { if (GUI.Button(new Rect(10, 85, 130, 35), "启动 / 重试")) Run().Forget(); }
            else if (!_destroyed && GUI.Button(new Rect(10, 85, 130, 35), "取消 / 停止")) Stop().Forget();
            if (_session?.RequiresRestart == true) GUI.Label(new Rect(150, 85, 700, 35), "代码加载已经尝试，重试须重启进程");
            GUI.Label(new Rect(10, 130, 1100, 500), _messages.ToString());
        }
    }
}
