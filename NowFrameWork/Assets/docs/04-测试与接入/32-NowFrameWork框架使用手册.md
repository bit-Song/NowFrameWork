# 32 · NowFrameWork 框架使用手册

本手册适用于当前团结工程。第一次接触框架时，先在已有样例里观察启动、资源使用和停止，再把需要的服务接进自己的入口。例子只使用框架测试资源和测试面板。

模块之间的关系、启动后的对象结构和常用 API 入口可以对照 [33 NowFrameWork 结构图与 API 导览](33-NowFrameWork结构图与API导览.md)。

需要按功能查询具体调用与释放方式时，使用 [模块 API 索引](../06-模块API/README.md)。

## 1. 第一次运行

在团结编辑器中打开 **Window → Asset Management → Addressables → Groups**，将 **Play Mode Script** 设为 **Use Asset Database (fastest)**。这是本地 Play 模式的样例配置。然后按下表顺序打开场景。

| 场景 | 操作 | 成功标志 |
|---|---|---|
| [BootIntegration.scene](../../Scenes/BootIntegration.scene) | 按 Play，等启动完成，点 Game 窗口左上角「取消 / 停止」。 | 先显示 `Running / Running / 100%`，停止后出现「停止与清理完成」。预设使用空配置、空附加场景和占位入口。 |
| [FrameworkApiSample.scene](../../Scenes/FrameworkApiSample.scene) | 按 Play 并等 `[API] 入口已就绪`。在 Hierarchy 选中 `FrameworkApiSample`，从组件右上角菜单执行「1.运行完整 API 流程」和「2.输出资源诊断」。 | Console 依次输出事件、计时器、配置、Prefab 池、附加场景与卸载结果。测试 Prefab 是空物体，在 Hierarchy 里看 `ApiSample_PooledInstance`。流程完成后，配置缓存仍可能持有一个租约，Boot 停止时归还。 |
| [UIIntegration.scene](../../Scenes/UIIntegration.scene) | 按 Play，依次试 Open、Hide、Show、Close 和 Reverse。 | Hide 后可以用同一会话 Show。Close 后重新 Open。Reverse 最终回到可见。 |
| [UIWindows.scene](../../Scenes/UIWindows.scene) | 点 Next page 进入 Inventory，再打开 Settings 和 Confirm。用 Back 逐层关闭，最后回到 Home。 | 上层模态持有输入，关闭后恢复下层焦点。Confirm 不响应遮罩点击，Settings 可以通过遮罩关闭。回到根页后继续 Back 不会关闭根页。 |
| [UITween.scene](../../Scenes/UITween.scene) | 观察 Home、Inventory 和弹窗的不同动画。体验缓存时，在进入 Play 前给 `UiWindowsSample` 勾选 **Cache Settings**。 | 关闭 Settings 后显示 **Trim cache (1)**。15 秒内重开可复用实例。按 **Trim cache**、等待空闲到期或停止 Boot，可以清理缓存。 |

每个场景都先点「取消 / 停止」，等停止完成再退出 Play。清理过程中需要 Unity 继续执行帧循环，比如等待已调用 `Destroy` 的对象真正消失。试资源、配置或 UI 时，先记住正常运行时有哪些常驻租约，再观察重复操作是否回到基线。 [手动 API 流程](23-Unity手动测试与API.md) · [UI 基础流程](25-UI最小流程与接入.md) · [多窗口测试](29-UI多窗口导航与Canvas规范.md)

## 2. 把启动入口接进场景

`BootFlow` 是场景里的 Unity 组件。它创建 `BootSession`，驱动 `App` 的帧循环，并在控制台记录启动和停止结果。`BootSession` 负责启动阶段、上下文和失败清理。`IBootEntry` 是你接入框架服务的地方。

启动顺序为 **创建上下文 → 读取设置 → 启动基础模块和初始化资源 → 可选资源更新 → 可选配置 → 可选代码加载 → 可选附加场景 → 入口初始化 → Running**。

### 2.1 最小入口脚本

在 `Assets/Game` 新建 `MinimalFrameworkEntry.cs`。把脚本挂到和 `BootFlow` 同一个对象，并在 Inspector 把该组件拖到 **Project Entry**。初次接入可以沿用 `BootIntegration.scene` 的配置，或者复制它作为自己的测试场景。

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using NowFrameWork.Boot;
using NowFrameWork.Core.Logging;
using UnityEngine;

public sealed class MinimalFrameworkEntry : MonoBehaviour, IBootEntry
{
    private GameEntry.Binding _binding;

    public UniTask EnterAsync(BootContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _binding = GameEntry.Bind(context);
        context.App.Log.Write(LogLevel.Info, "FRAMEWORK", "入口已就绪");
        return UniTask.CompletedTask;
    }

    public UniTask ExitAsync()
    {
        _binding?.BeginStopping();
        _binding?.Dispose();
        _binding = null;
        return UniTask.CompletedTask;
    }
}
```

在已有 `Project Entry` 引用的样例场景中，原有入口负责模块装配和绑定。接入自己的模块时，先启动模块，再调用 `GameEntry.Bind(context, ui)`。开始退出时调用 `BeginStopping()`，取消并等待自己发起的任务，清理所持有的资源，然后解绑。若清理失败，保留相关字段和绑定供下一次 Stop 重试。上面这个最小入口只持有绑定，因此可以立即解绑。

### 2.2 什么时候可以取服务

入口初始化用传入的 `context`。其他代码可以在 `GameEntry.IsReady` 为真后访问 `GameEntry.Assets`、`Configs`、`Settings`、`Log`、`Events`、`Clock` 和 `Timers`。在就绪后用 `HasUI` 和 `HasScenes` 判断可选服务是否已接入。`GameEntry` 就绪只表示服务已绑定。需要依赖 Boot 帧驱动的操作，例如等待 `App.Timers` 触发，应在 Boot 进入 `Running` 后执行。

场景对象的 `Awake` 或 `Start` 可能早于入口绑定。可以由入口显式启动依赖服务的对象，或者让这些对象等待就绪。框架 API、Unity 对象操作以及会恢复到框架代码的回调和取消操作应遵守主线程约定。

如果脚本属于自己的 asmdef，在 Inspector 的 **Assembly Definition References** 添加实际使用的程序集，例如 `NowFrameWork.Boot`、`NowFrameWork.Core`、`NowFrameWork.Unity`、`NowFrameWork.Assets` 和 `UniTask`。调用 `GameEntry` 需要引用 `Game`。直接使用 `BootFlow` 需要 `NowFrameWork.Boot.Addressables`，使用 UI 类型需要 `NowFrameWork.UI.UGUI`。当前工程的 `Game` 程序集可以作为参考。 [Boot 启动整合](21-Boot启动整合.md) · [GameEntry 生命周期](28-GameEntry服务入口与生命周期.md)

## 3. 资源、配置和场景

### 3.1 加载资源与归还租约

在 Addressables Groups 给资源登记地址。代码可以传地址字符串、`AssetKey` 或生成的键。以下片段放在接收 `CancellationToken ct` 的异步方法中，`context` 来自入口。

```csharp
using (var lease = await context.Assets.AcquireAsync<TextAsset>("PCProbe/Config", ct))
{
    Debug.Log(lease.Asset.text);
}
```

每次申请都会得到一份独立租约，同资源的底层加载可以共享。只在租约有效期间使用 `lease.Asset`。若用 Prefab 创建长寿命实例，保存实例和对应租约，先销毁实例并等待真正销毁，再归还租约。反复创建和回收的对象可以交给下一节的 `AssetPool<T>`。

如果 Provider 释放失败，保留原租约并重试 `Dispose()`。释放开始后，租约的 `Asset` 已不可继续使用。加载异常优先检查地址、资源类型、分组和所用的 Play Mode Script。

### 3.2 生成资源键和读取常驻配置

在 **NowFrameWork → Assets → 资源键生成器** 中选择资源根目录，先扫描并处理冲突，再预览并确认生成。生成文件是 `Assets/NowFrameWork/Generated/AssetKeys.cs`，调用方可以引用 `NowFrameWork.Generated.AssetKeys`。本仓库当前的测试配置键为 `NowFrameWork.Generated.AssetKeys.Tests.PCRegression.Config_txt`。重新选择扫描根目录会影响生成的类层级。 [资源键生成器](24-Addressables资源键生成器.md)

配置通常使用 `context.Configs.LoadAsync<T>()`，由 `ConfigService` 持有缓存租约，Boot 停止时统一清理。将配置作为只读数据使用。若把地址填进 BootFlow 的 **Config Address**，该项改为启动所必需，缺失会使 Boot 在 `Config` 阶段失败。

```csharp
TextAsset config = await context.Configs.LoadAsync<TextAsset>("PCProbe/Config", ct);
Debug.Log(config.text);
```

### 3.3 加载和卸载附加场景

`LoadAdditiveAsync` 返回 `SceneLease`。如果需要自行控制其生命周期，把租约保存在能负责退出清理的字段里。只有在卸载成功后才清空字段。

```csharp
_scene = await context.Scenes.LoadAdditiveAsync("PCProbe/Scene", ct);

// 在负责退出的异步方法里执行。
await _scene.UnloadAsync();
_scene = null;
```

如果 **Scene Address** 由 BootFlow 配置，则这份场景租约由 Boot 持有和卸载。通过入口直接申请的附加场景由入口自己卸载。测试场景详情见现有 API 样例。 [资源、配置和场景示例](23-Unity手动测试与API.md)

## 4. 对象池、模块和 Scope

- **普通对象池**。用 `context.App.Pools.GetOrCreate<T>(key, create, destroy)` 创建或取得池，`Get()` 借出，`Release(item)` 归还。退出时由持有者调用 `DisposePool(key)` 移除在 App 池服务中登记的池。先处理所有借出的对象。
- **Prefab 资源池**。用 `AssetPool<GameObject>.CreateAsync(...)` 建池，由池保留一份 Prefab 租约。用 `Rent()` 和 `Release(item)` 管理对象，退出时 `await pool.DisposeAsync()`。池会等待实例真正销毁，再归还保活租约。可以参考 `FrameworkApiSample.TestPoolAsync` 的完整生命周期。
- **模块**。实现 `IAppModule`，声明依赖、初始化、启动和关闭。在所属 `ModuleHub` 启动前注册。进入 `IBootEntry.EnterAsync` 时，基础 App Hub 已启动，入口自有模块可以注册到入口创建的独立 Scope。
- **Scope**。用来表示一组有共同寿命的模块、对象池和子 Scope。创建后注册模块，再 `await scope.StartupAsync(ct)`。需要划分更短的生命周期时，在运行中的 Scope 上 `CreateChild(name)`，再启动子作用域。退出时 `await scope.DisposeAsync()`。临时取得的租约仍由申请者管理。使用 UI 或 Prefab 池等有专用异步清理的服务时，先等待其清理成功再释放上层 Scope。

资源池、场景租约和安装的模块应有清晰的退出位置，建议和入口的 `ExitAsync()` 一起检查。 [对象池与资源租约](../03-模块设计/11-对象池与资源租约.md) · [Scope 和资源层](../03-模块设计/13-Scope与资源层设计.md)

## 5. UI 面板、导航、动画与缓存

手动搭 UI：菜单 **NowFrameWork → UI** 下三个入口。

| 菜单 | 生成内容 |
|---|---|
| 在场景中创建 UiRoot | Canvas、SafeArea 与 Page / Popup / Toast 三层；已有则只选中 |
| 在场景中创建基础 UI 层级 | UiRoot + EventSystem（检测到 Input System 时自动挂模块）+ 一个面板骨架 |
| 在场景中创建面板 | 面板骨架：Canvas / CanvasGroup / GraphicRaycaster / UiView + SafeArea / Layout / Header / Actions / Status |

面板骨架可直接拖到 Project 存成 Prefab，再通过 `UiDefinition` 注册地址。

### 5.1 数据化注册与 GameEntry 入口

不再手写 `UiDefinition` 数组：新建 **Create → NowFrameWork/UI/UI Catalog** 资产，在 Inspector 填面板 ID、名称、Address 与层级。

项目入口只需一次装配：

```csharp
await GameEntry.EnableUIAsync(context, uiRootPrefab, uiCatalog, eventSystem, ct);
// 之后全局直接使用：
var opened = await GameEntry.OpenPanelAsync(10, new MyArgs { Title = "Hi" });
```

| API | 作用 |
|---|---|
| `GameEntry.EnableUIAsync(...)` | 创建并启动 `UiModule`，同时绑定 `GameEntry`（必须在 `Bind` 之前调用） |
| `GameEntry.OpenPanelAsync(id, args)` | 按 Catalog 的 ID 打开面板，args 原样传给 `UiView.OnOpenAsync` |
| `GameEntry.ShutdownUIAsync()` | 停止并释放 UI，重复调用无副作用 |
| `GameEntry.UI` | 完整的 `IUiService`（导航、显隐、关闭、缓存） |

退出时顺序：先停业务在途操作 → `GameEntry.ShutdownUIAsync()` → `binding.Dispose()`。

### 5.2 自动生成绑定脚本

菜单 **NowFrameWork → UI → Config** 打开配置窗口：输出目录（默认 `Assets/NowFrameWork/UI/Generated`）、命名空间、`public`/`[SerializeField] private`、首字母大小写、是否识别 TextMeshPro、是否自动推断未标记组件（默认关闭）、生成后是否写入引用。

在 Hierarchy 或 Project 中右键 UI 根节点，选择 **NowFrameWork → UI → 生成 UI 绑定脚本**；也可以在 `UiView` 组件右键。

| 规则 | 说明 |
|---|---|
| **只生成标记节点** | 只有 `[Button]test1` 这类名字才生成字段；未标记节点默认忽略，避免把装饰 UI 全部生成出来 |
| 可选推断 | 勾选"自动推断未标记组件"后，才按节点上的 `Button`/`Text`/`Image` 等组件生成 |
| 标记覆盖 | `[Button]test1` → `Button test1`；支持 `[Text] [Image] [Slider] [Toggle] [Scroll] [Input] [Rect] [TMP] [TMPInput]` |
| 命名 | 去标记、非法字符转 `_`、可选首字母小写、重名自动加序号 |
| 生成位置 | 已有 `UiView` 脚本就地更新；没有则在配置目录新建 `XxxView : UiView` |
| 保留手写代码 | 字段在 `// <auto-generated>`，事件在 `// <auto-generated-events>`，逻辑写在 `// <user-code>`；前两块每次生成替换，user-code 只增不删 |
| 事件骨架 | 按钮自动生成 `onClick.AddListener(OnXxxClicked)` 与 `partial void OnXxxClicked();` 声明 |
| 生命周期 | 可选生成 `OnOpenAsync` / `OnCloseAsync` 重写骨架，放在 user-code 内 |
| 分组 | 按节点父路径生成 `#region`；不生成嵌套类，避免改变字段访问路径与序列化结构 |
| 覆盖检查 | 生成前显示已绑定 / 未绑定 / 将移除字段，移除项单独列出 |
| 免拖拽 | 生成时用 `SerializedObject` 直接写入引用；运行时不调用 `GetComponent`、不做查找 |
| Prefab | 选中 Prefab 资产时通过 `LoadPrefabContents` 修改并保存，不产生场景覆盖 |

新建脚本第一次生成后需要编译：编译完成时框架会自动挂载脚本并写入引用，无需再点一次；已存在的脚本则即时更新。

### 5.1 接入一个面板

1. 准备 `UiRoot` 和一个有效的 `EventSystem`。从现有样例复用 UI Root 的配置可减少层级错误，Root 下有 Page、Popup 和 Toast 容器。
2. 让面板根节点挂载 `UiView` 派生组件，配好 Canvas、CanvasGroup 和所需的 GraphicRaycaster，并登记其 Prefab 的 Addressables 地址。
3. 用 `UiDefinition` 将项目内唯一的数字 ID 对应到 Prefab。用相同 ID 创建 `UiKey<TArgs>`，参数类型是引用类型。无参数面板使用 `UiKey<EmptyArgs>` 与 `EmptyArgs.Default`。
4. 在入口创建 `UiModule`，传入资源服务、Root、面板定义、默认动画和 EventSystem。等待模块初始化、启动后，再把 `module.Service` 交给 `GameEntry.Bind(context, module.Service)`。参考 `UiWindowsSample.EnterAsync` 或 `UiFlowSample.EnterAsync`。

下面是入口装配片段。`_rootPrefab` 和 `_eventSystem` 由 Inspector 赋值，`_module` 和 `_binding` 由入口持有，退出时先等待模块的 `DisposeAsync()` 再解绑。

```csharp
var definition = new UiDefinition(1, "SamplePanel", AssetKey.From("UiProbe/Panel"));
_module = new UiModule(context.Assets, _rootPrefab, new[] { definition },
    new CanvasGroupUiTransition(), _eventSystem, log: context.App.Log);
await _module.InitializeAsync(ct);
await _module.StartAsync(ct);
ct.ThrowIfCancellationRequested();
_binding = GameEntry.Bind(context, _module.Service);
```

下面的操作片段在模块绑定后执行。为了在画面上观察各状态，可以把每个操作绑定到独立的测试按钮。 [最小 UI 样例源码](../../Game/UiFlowSample.cs)

```csharp
var panelKey = new UiKey<EmptyArgs>(1, "SamplePanel");
var ui = GameEntry.UI;
var opened = await ui.OpenAsync(panelKey, EmptyArgs.Default, ct);
if (!opened.IsOpen) return;

await ui.HideAsync(opened.Handle, ct);
await ui.ShowAsync(opened.Handle, ct);
await ui.CloseAsync(opened.Handle, ct);
```

打开结果为 `Ok` 或 `AlreadyOpen` 时能取得有效句柄。`Busy` 表示已有相关操作在途，应等当前操作结束。Hide 保留会话和资源，Show 恢复该会话。Close 结束会话，旧句柄不能控制下次打开的会话。Show、Hide、Close 和导航的调用者取消只结束本次等待，服务仍可能在收尾。

面板在 `OnOpenAsync` 接收本次参数，在 `OnCloseAsync` 清理会话里的任务和订阅。需要跨缓存复用保留的组件初始化可放在 `OnCreate`，最终解绑放在 `OnDispose`。只在可见期间运行的任务使用 `VisibleToken`，维持到会话关闭的任务使用 `SessionToken`。 [面板生命周期和 UI 接入](25-UI最小流程与接入.md)

### 5.2 页面、模态和返回

页面栈使用 `ui.Navigation.PushAsync(key, args, ct)` 和 `PopAsync(ct)`。Push 会隐藏前页，Pop 恢复前页并关闭当前页。导航栈拥有这些页面的显隐和关闭，项目通过导航 API 操作它们。

弹窗定义使用 `Layer = UiLayer.Popup`。需要阻挡背景输入时设置 `Modal = true`，允许点击遮罩关闭时再设置 `CloseOnBackdrop = true`。模态窗口之间共用一张遮罩。设置 `UiView.DefaultFocus` 可指定窗口获得焦点时的默认控件。使用 `await ui.RequestBackAsync(ct)` 分发项目返回动作，框架先处理弹窗，再处理页面历史。 [多窗口与返回规则](29-UI多窗口导航与Canvas规范.md)

### 5.3 动画和面板缓存

默认过渡由 `UiModule` 装配，可以使用即时切换或 `CanvasGroupUiTransition`。要给部分面板设置独立动画，在面板根节点配置启用的 `UiTransitionBehaviour`。使用 DOTween 适配时，在 **Assets → Create → NowFrameWork → UI → Tween Preset** 创建预设，然后为 `DOTweenUiTransition` 配置预设和动画节点。位移、缩放应绑定到面板内的 MotionRoot。 [DOTween 过渡接入](30-UI面板动画与DOTween接入.md)

给某个定义设置 `CacheOnClose = true` 后，该面板关闭时可以保留空闲实例和 Prefab 租约。模块用 `UiCacheOptions` 控制容量与空闲时间。默认容量为 16、空闲 60 秒，容量 0 会禁用缓存。`CachedCount` 表示当前可复用的空闲实例，`await ui.TrimCacheAsync(ct)` 可清理空闲缓存，Boot 停止时也会清理。缓存里的实例再次打开时仍会收到新参数和新句柄。 [面板缓存接入](31-UI面板缓存与接入.md)

## 6. 设置、日志、事件和计时器

### 6.1 读写设置

Boot 已加载 `context.Settings`。修改内存值后调用 `Save()` 才会写盘。在 `FrameworkApiSample.scene` 可以用组件菜单「3.保存测试语言 zh-CN」试一次，重新启动后查看读取结果。

```csharp
context.Settings.Value.Language = "zh-CN";
context.Settings.Save();
Debug.Log(context.Settings.FilePath);
```

样例的 `Language` 是保存下来的设置数据。应用语言要由接入层处理。要保存自己的设置 DTO，可用 `FileSettingsStore<T>` 配 `JsonSettingsCodec<T>`，明确数据版本和校验规则。标记默认值、主文件和备份恢复的结果来自 `Load()`。文件放在平台的持久化目录，文件路径可通过 `FilePath` 查看。 [存储与设置设计](../03-模块设计/19-存储与设置设计.md)

### 6.2 日志、事件和计时器

```csharp
// using NowFrameWork.Core.Logging;
context.App.Log.Write(LogLevel.Info, "FRAMEWORK", "测试入口已启动");

// 事件类型使用 struct。订阅令牌应由订阅者持有和释放。
using (context.App.Events.Subscribe<SampleSignal>((in SampleSignal evt) => Debug.Log(evt.Value)))
{
    context.App.Events.Publish(new SampleSignal { Value = 42 });
}

```

`SampleSignal` 可定义为 `struct SampleSignal { public int Value; }`。即时发布用 `Publish`，希望由 App 帧循环处理时可以用 `PublishDeferred`。

计时器使用秒为单位。`Delay`、`Interval` 走缩放时钟，`DelayUnscaled`、`IntervalUnscaled` 走无缩放时钟。Boot 进入 Running 后调用 `context.App.Timers.DelayUnscaled(0.5, () => Debug.Log("计时完成"))`。把返回的 `TimerHandle` 存为入口字段，在入口退出时调用 `context.App.Timers.Cancel(handle)`。

`context.App.Platform` 提供平台信息，`context.App.Lifecycle` 提供应用生命周期信号。BootFlow 转发 Unity 的暂停、焦点和退出事件。可以在入口按需要接入这些信号，并在退出时解除订阅。 [核心 API 样例](23-Unity手动测试与API.md) · [主线程约束](../02-开发规范/18-主线程约束与验证.md)

## 7. 资源更新、热更与诊断

### 7.1 资源更新

首次上手优先用本地样例。需要验证资源更新时，在项目启动流程中设置 **Check Resource Updates** 和 **Update Key**，让 Boot 在申请配置、场景、实例或 DLL 租约之前更新。也可以在独立的更新入口中创建 `HotUpdateService`，按顺序调用 `PrepareAsync(key, ct)` 与 `DownloadAsync(key, progress, ct)`。Update Key 应覆盖启动资源及其依赖。

HybridCLR 代码加载通过可选的 `HybridClrBootLoader` 接入。只有明确启用代码热更时才在 BootFlow 配置 **Load Code** 和 **Code Loader**。一旦代码加载已尝试，进程必须重启才能重新进行启动尝试。远端 Catalog、DLL 与 AOT 元数据的验证用既有 PC 回归夹具。 [Windows 资源与热更回归](17-PC回归入口与实测.md) · [Boot 热更适配](21-Boot启动整合.md)

### 7.2 排查问题

| 现象 | 检查位置 |
|---|---|
| Play 后没有正常启动 | 看 Console 第一条错误和 `[Boot]` 报出的阶段。若停在 Config，核对 **Config Address** 是否已登记且资源类型正确。 |
| 资源加载失败 | 检查 Address、分组、资源类型和 Play Mode Script。运行本地样例时先用 Asset Database 模式。 |
| 访问 `GameEntry` 报尚未就绪 | 检查入口是否完成绑定，以及调用是否早于入口。对于 UI 和场景服务，也要确认其可选服务已经装配。 |
| UI 打开后无输入 | 检查样例是否配置了有效的 EventSystem 和 GraphicRaycaster，关注顶层模态、显隐状态和导航过渡是否正在阻挡输入。 |
| Close 后租约未少 | 检查是否启用了面板缓存、是否还有同资源的实例持有租约，以及清理是否仍在等待销毁。使用 `CachedCount` 或 `TrimCacheAsync()` 区分空闲缓存。 |
| Stop 报「清理未完成，可重试」 | 找到未释放的池、场景、租约或失败回调，修复后再点停止。清理成功后才能开始新的 Boot 上下文。 |
| 编译提示找不到框架命名空间 | 检查当前脚本所属 asmdef 的引用，尤其是 `Game`、`NowFrameWork.Boot.Addressables`、`NowFrameWork.UI.UGUI` 和生成资源键程序集。 |
| Addressables Groups 重绘时索引越界 | 先关闭并重开 Groups，再区分新出现的 Console 报错和上次编辑器错误。 [已记录的复测](../05-进度与评审/15-进度与下一步.md#53-addressables-groups-报错与-uitween-复测) |

资源问题可以用 `FrameworkApiSample` 的「2.输出资源诊断」查看 `Pending`、`ActiveLeases` 和租约明细。项目也可以在编辑器或 Development Player 中引用 `NowFrameWork.Diagnostics`，用 `DiagnosticsOverlay.Install(context.Assets, context.App.Errors)` 安装诊断面板，退出时管理该面板的生命周期。正式构建会返回空引用。

修改框架后，可以从 **NowFrameWork → Tests → 运行全部 EditMode 测试** 验证回归。当前实现状态、已验收平台和后续模块以进度文档为准。键盘与手柄的控件焦点导航和虚拟列表见后续方案。 [进度与下一步](../05-进度与评审/15-进度与下一步.md) · [验收证据](../evidence/README.md)

## 8. 服务速查

| 需要什么 | 从哪里取 | 接口 |
|---|---|---|
| 资源 | `context.Assets` / `GameEntry.Assets` | `IAssetService` |
| 配置 | `context.Configs` / `GameEntry.Configs` | `ConfigService` |
| 设置 | `context.Settings` / `GameEntry.Settings` | `FileSettingsStore<BootPreferences>` |
| 场景 | `context.Scenes` / `GameEntry.Scenes` | `ISceneService` |
| UI | `GameEntry.UI` | `IUiService` |
| 音频 | `GameEntry.Audio` / `context.Audio` | `AudioModule` |
| 动作 | `GameEntry.Actions` | `ActionInput` |
| 日志 / 事件 / 时钟 / 池 | `context.App.Log/Events/Clock/Pools` | core 接口 |

先判断 `GameEntry.IsReady`/`HasUI`/`HasAudio` 再取服务；可选服务未装配时取用会抛异常，而不是返回 null。

## 9. 音频

音频模块随 Boot 注册，入口在 `GameEntry.Audio`。资源必须是 Addressables 条目（样例：`audio/bgm`、`audio/sfx`）。

```csharp
await GameEntry.Audio.PlayMusicAsync("audio/bgm");   // 循环 BGM，后一次请求替换前一次
await GameEntry.Audio.PlayEffectAsync("audio/sfx");  // 并发音效，受 maxEffects 限制
GameEntry.Audio.SetVolumes(0.8f, 1f);                // 0..1
GameEntry.Audio.StopMusic();
```

| 行为 | 说明 |
|---|---|
| 后台暂停 | 由 `IAppLifecycle` 驱动，`IsPaused` 可查；回前台继续，不从头播 |
| 通道上限 | 音效满时返回 `false`，不抛异常，调用方决定是否丢弃 |
| 资源释放 | 切歌/停止后，无引用的 Clip 租约自动释放 |
| 平台差异 | 非 Windows x64 跳过发布恢复，但音频本身仍走随包 Catalog |

常见坑：音频资源只放进资源目录、没标 Addressable，运行时报「找不到唯一资源」。

## 10. 输入

| 层 | 职责 |
|---|---|
| `IActionSource` | 设备适配：Input Actions、手柄、虚拟摇杆 |
| `ActionInput` | 多来源合并、门控、取值 |
| `InputModule` | 返回键分发：一帧一次、后台不补发、异常隔离 |
| `IBackSource` / `IBackFallback` | 返回键来源 / UI 不处理时的兜底 |

```csharp
var input = new ActionInput(
    new InputActionsSource(asset, (Actions.Move, "Player/Move")),
    new JoystickActionSource(joystick, Actions.Move));

Vector2 move = input.GetVector(Actions.Move);
input.SetGate("ui.modal", topModal);   // 每帧同步，幂等
```

| 约定 | 说明 |
|---|---|
| 门控分域 | `SetGate` 是幂等开关；`Suppress` 返回 `IDisposable` 令牌，两者可叠加 |
| 热路径轮询 | 用 `GetVector`/`WasPressed`，不要再订阅动作回调，避免双触发 |
| 二维动作 | `ActionState.Value` 是长度（强度），方向用 `Vector2 Vector` |
| 改键 | 复合动作要按 binding 索引逐方向改；同 ID 冲突只提示同一 Action Map |

## 11. 本地化

Unity Localization 负责查表、回退与格式化；项目接入层负责语言选择与保存。

```csharp
await language.InitializeAsync(store.Load(() => new BootPreferences()).Source, ct);
await language.SwitchAsync(LanguageSelection.Chinese, ct);
```

| 约定 | 说明 |
|---|---|
| 缺译回退 | 表缺键回退英文，不抛错 |
| 持久化 | 选择写入 `BootPreferences`，重启恢复 |
| 字体 | 中文需自带字体；当前按横屏设计，竖屏适配未做 |

## 12. Windows 资源发布与恢复

```csharp
await releases.StageFromServerAsync(new Uri("http://127.0.0.1:8080/v2/release.json"), ct);
// 暂存成功 → 下次启动试用 → Boot 进入 Running 才确认
```

| 规则 | 说明 |
|---|---|
| 清单校验 | 每个文件记录长度与 SHA-256；坏文件/断流失败且不落盘 |
| 同 ID 拒绝 | 不能用当前确认版本 ID 再暂存，重发内容必须换 ID |
| 回退 | 试用期失败或强杀，重启回到已确认版本 |
| 平台 | 非 Windows x64 跳过发布选择，直接用随包内容 |

验收脚本 `Tools/ReleaseKillProbe.ps1` 可复现「暂存 → 试用 → 强杀 → 回退」。

## 13. 层级状态机

```csharp
var fsm = new StateMachine<Phase>();
fsm.AddState(Phase.Ready, new ReadyState());
fsm.SetInitialState(Phase.Ready);
fsm.AddEventTransition(Phase.Ready, "Begin", Phase.Active);
fsm.Enter();
```

| 约定 | 说明 |
|---|---|
| 回调内请求 | 状态回调里的切换在回调结束后统一处理 |
| 退出协调 | 实现 `IStateExitHandler` 可延迟切换；强制规则可打断等待 |
| 异常 | 回调抛错停止活动链，用 `Faulted`/`FaultPath`/`LastException` 定位 |
| 清理重试 | `Exit()` 失败保留待清理状态，`HasPendingCleanup` 后重试 |

## 14. 取消、错误与性能

| 规则 | 做法 |
|---|---|
| 取消是正常路径 | `OperationCanceledException` 不记 Error |
| 即发即忘 | 走 `TaskRunner.Run`，异常进 `ErrorBudget` |
| 等待外部资源 | 销毁/卸载必须带超时与重试路径，失败保留句柄 |
| 热路径零分配 | 避免闭包、LINQ、字符串拼接；缓存委托 |
| 断言 | `MainThreadGuard.Assert` 带 `[Conditional]`，正式包零成本 |
| 资源计数 | `ActiveLeases`/`PendingRequests` 用于验收，不要每帧轮询 |

## 15. 测试与 CI

| 入口 | 用途 |
|---|---|
| 菜单 `NowFrameWork → Tests → 运行全部 EditMode 测试` | 编辑器内全量（481 条） |
| `Tools/Run-FrameworkRegression.ps1` | 预检 + 主工程 + 两套消费工程 |
| GitHub Actions `Framework Regression` | 自托管 runner；失败也上传 artifact |
| PlayMode | 本地交互验证；batchmode 下不可靠，不纳入 CI |

CI 排障顺序：看 `run.json` 阶段名 → 对应 `.log`/`.xml` → `ci-step.log`。
