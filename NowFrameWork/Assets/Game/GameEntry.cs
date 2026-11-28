using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NowFrameWork.Assets;
using NowFrameWork.Boot;
using NowFrameWork.Core.Events;
using NowFrameWork.Core.Logging;
using NowFrameWork.Core.Storage;
using NowFrameWork.Core.Threading;
using NowFrameWork.Core.Time;
using NowFrameWork.Input;
using NowFrameWork.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>
    /// 仅主线程的项目服务入口。组合根在模块就绪后绑定；不创建、初始化或释放服务。
    /// 场景/战斗状态不放在这里；缓存的服务引用仍须遵守原服务的取消和资源所有权约定。
    /// </summary>
    public static class GameEntry
    {
        static Binding _current;
static UiModule _uiModule;
static UiCatalog _uiCatalog;
static EventSystem _ownedEventSystem;

        public static bool IsReady
        {
            get { MainThreadGuard.Assert("GameEntry.IsReady"); return _current != null && !_current.Stopping; }
        }

        public static IAssetService Assets => Current.Context.Assets;
        public static ConfigService Configs => Current.Context.Configs;
        public static FileSettingsStore<BootPreferences> Settings => Current.Context.Settings;
        public static ILog Log => Current.Context.App.Log;
        public static EventBus Events => Current.Context.App.Events;
        public static IClock Clock => Current.Context.App.Clock;
        public static TimerService Timers => Current.Context.App.Timers;
        public static bool HasUI => Current.UI != null;
        public static bool HasScenes => Current.Context.Scenes != null;
        public static bool HasAudio => Current.Context.Audio != null;

        /// <summary>音频模块；Boot 已默认注册，未启用时抛错而不是返回 null。</summary>
        public static NowFrameWork.Audio.AudioModule Audio => Current.Context.Audio
            ?? throw new InvalidOperationException("当前入口未启用音频模块。");
        public static IUiService UI => Current.UI
            ?? throw new InvalidOperationException("当前项目入口未启用 UI 模块。");

        public static bool HasActions => Current.Actions != null;

        /// <summary>动作读取与输入屏蔽入口；未启用时抛错，避免静默拿到 null 后误判"没有输入"。</summary>
        public static ActionInput Actions => Current.Actions
            ?? throw new InvalidOperationException("当前项目入口未启用动作输入。");
        public static ISceneService Scenes => Current.Context.Scenes
            ?? throw new InvalidOperationException("当前项目入口未启用场景服务。");

        static Binding Current
        {
            get
            {
                MainThreadGuard.Assert("GameEntry.Service");
                if (_current == null) throw new InvalidOperationException("GameEntry 尚未就绪或已停止，请在项目入口绑定完成后调用。");
                if (_current.Stopping) throw new InvalidOperationException("GameEntry 正在停止，不再接受新的业务访问。");
                return _current;
            }
        }

        /// <summary>只由项目组合根调用；调用前须完成 Boot 基础服务与所选项目模块的启动。</summary>
        public static Binding Bind(BootContext context, IUiService ui = null, ActionInput actions = null)
        {
            MainThreadGuard.Assert("GameEntry.Bind");
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (_current != null) throw new InvalidOperationException("GameEntry 已绑定或尚未完成停止；禁止覆盖服务，请先由原所有者完成清理。");
            ui = ui ?? context.UI?.Service;
        var binding = new Binding(context, ui, actions);
            _current = binding;
            return binding;
        }

        // 即使关闭 Domain Reload，每次进入 Play 也不会沿用上次的静态引用。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
{
    _current = null;
    _uiModule = null;
    _uiCatalog = null;
    _ownedEventSystem = null;
}

        /// <summary>一次绑定的所有者凭据。清理失败时保留它，停止状态禁止重新绑定；重试成功后再 Dispose。</summary>
        /// <summary>装配并启动 UI 模块；未绑定时顺带绑定 GameEntry。只应在项目入口调用一次。</summary>
public static async UniTask<UiModule> EnableUIAsync(BootContext context, UiCatalog catalog, UiRoot rootPrefab = null,
EventSystem eventSystem = null, ActionInput actions = null, CancellationToken ct = default)
{
MainThreadGuard.Assert("GameEntry.EnableUIAsync");

if (context == null) throw new ArgumentNullException(nameof(context));
if (catalog == null) throw new ArgumentNullException(nameof(catalog));
if (context.UI != null) throw new InvalidOperationException("UI 已由 Boot 启用，无需再次 EnableUIAsync。");
if (_uiModule != null) throw new InvalidOperationException("UI 已启用，请先 ShutdownUIAsync。");
if (_current != null) throw new InvalidOperationException("UI 必须在 GameEntry.Bind 之前启用。");

EventSystem events = eventSystem;

if (events == null)
{
events = UiRootFactory.EnsureEventSystem();
_ownedEventSystem = events;
}

UiRoot createdRoot = rootPrefab == null ? UiRootFactory.Create(null, events) : null;

var module = new UiModule(context.Assets, rootPrefab, catalog.BuildDefinitions(),
new CanvasGroupUiTransition(), events, log: context.App.Log, rootInstance: createdRoot);

await module.InitializeAsync(ct);

try
{
await module.StartAsync(ct);
}
catch
{
await module.DisposeAsync();
throw;
}

_uiModule = module;
_uiCatalog = catalog;
Bind(context, module.Service, actions);

return module;
}

/// <summary>停止并释放 UI 模块；重复调用无副作用。</summary>
public static async UniTask ShutdownUIAsync()
{
UiModule module = _uiModule;
_uiModule = null;
_uiCatalog = null;

if (module != null) await module.ShutdownAsync(CancellationToken.None);

if (_ownedEventSystem != null)
{
UnityEngine.Object.Destroy(_ownedEventSystem.gameObject);
_ownedEventSystem = null;
}
}

/// <summary>按面板 ID 关闭：默认关闭最上层一个，all 为 true 时关闭全部。返回关闭数量。</summary>
public static UniTask<int> ClosePanelAsync(int id, bool all = false, CancellationToken ct = default)
{
MainThreadGuard.Assert("GameEntry.ClosePanelAsync");

if (_uiModule == null) throw new InvalidOperationException("UI 尚未启用，请先在项目入口调用 EnableUIAsync。");

return _uiModule.Service.CloseAsync(id, all, ct);
}

/// <summary>按 Catalog 中的面板 ID 打开面板；args 原样传给 UiView.OnOpenAsync。</summary>
public static UniTask<UiOpenResult> OpenPanelAsync(int id, object args = null, CancellationToken ct = default)
{
MainThreadGuard.Assert("GameEntry.OpenPanelAsync");

if (_uiModule == null) throw new InvalidOperationException("UI 尚未启用，请先在项目入口调用 EnableUIAsync。");
if (_uiCatalog == null || !_uiCatalog.TryGet(id, out UiCatalog.Entry entry))
throw new InvalidOperationException("UiCatalog 中没有面板 ID：" + id);

return _uiModule.Service.OpenAsync(new UiKey<object>(id, entry.Name), args ?? new object(), ct);
}

public sealed class Binding : IDisposable
        {
            internal BootContext Context { get; private set; }
            internal IUiService UI { get; private set; }
            internal ActionInput Actions { get; private set; }
            internal bool Stopping { get; private set; }

            internal Binding(BootContext context, IUiService ui, ActionInput actions)
            {
                Context = context;
                UI = ui;
                Actions = actions;
            }

            public void BeginStopping()
            {
                MainThreadGuard.Assert("GameEntry.BeginStopping");
                if (ReferenceEquals(_current, this)) Stopping = true;
            }

            /// <summary>仅解绑，不关闭模块。必须先完成项目业务和模块清理；重复调用安全，旧凭据不能清空新绑定。</summary>
            public void Dispose()
            {
                MainThreadGuard.Assert("GameEntry.Unbind");
                if (ReferenceEquals(_current, this)) _current = null;
                Stopping = true;
                Context = null;
                UI = null;
                Actions = null;
            }
        }
    }
}
