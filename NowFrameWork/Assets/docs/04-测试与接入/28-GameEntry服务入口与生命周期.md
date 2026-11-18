# 28 · GameEntry 服务入口与生命周期

> 日期：2026-11-18。项目通过 `Game.GameEntry` 访问应用共享的服务，服务仍由 Boot 与项目模块创建、初始化和释放。选型背景见 [27](../03-模块设计/27-依赖传递方案对比.md)。

## 1 初始化顺序

1. Boot 初始化设置、基础模块和资源服务，按配置完成更新、代码和场景准备。
2. Boot 调用项目 `IBootEntry.EnterAsync`，传入本次启动的 `BootContext`。
3. 项目入口通过显式依赖启动所需模块，例如使用 `context.Assets` 启动 UiModule。
4. 模块启动成功且未取消后，调用 `GameEntry.Bind(context, ui)`，保存返回的 Binding。
5. 业务开始运行，通过 `GameEntry.Assets` 或 `GameEntry.UI` 调用服务。

绑定一次发布本次入口选择的服务。GameEntry 不判断任意服务是否真正初始化完成，组合根负责遵守第 3、4 步；不会按需自动创建模块。只启动资源示例时可省略 ui，不必为不存在的音频、网络模块设置空占位。

场景对象的 Awake/Start 可能早于第 4 步，尤其是 Boot 在进入项目入口前加载的场景。依赖服务的业务应由项目入口在绑定后显式启动，或在自身生命周期内等待就绪；仅把脚本挂到场景上不代表服务已经可用。

## 2 业务调用

```csharp
using Game;
using NowFrameWork.Generated;
using UnityEngine;

// 在项目入口绑定完成后调用；ct 来自持有者的生命周期。
using (var lease = await GameEntry.Assets.AcquireAsync<TextAsset>(
    AssetKeys.Tests.PCRegression.Config_txt, ct))
{
    Debug.Log(lease.Asset.text);
}
```

Prefab 加载仍需由工厂或业务持有租约，先确认克隆实例真正销毁，再归还租约。统一入口不接管实例、租约、事件订阅或计时器的释放。

| 入口 | 指向已有对象 |
|---|---|
| Assets | Boot 的 IAssetService |
| Configs / Settings | Boot 的配置服务、小型设置存储 |
| Log / Events / Clock / Timers | Boot App 的日志、事件、时钟和计时器 |
| UI | 项目已启动并传入的 IUiService；可选 |
| Scenes | BootContext 中的 ISceneService；可选 |

先用 `IsReady` 判断入口是否可访问。就绪后，`HasUI` / `HasScenes` 可判断可选服务是否提供；未提供时直接读取对应服务会抛出带模块名称的异常。未绑定、已解绑或停止期间，服务属性和可选服务查询都会拒绝访问。

这些入口只允许主线程使用；后台工作结束后先切回主线程。

## 3 项目入口的所有权

```csharp
GameEntry.Binding _binding;

// EnterAsync 内：基础服务与项目模块都准备好后。
ct.ThrowIfCancellationRequested();
_binding = GameEntry.Bind(context, _uiModule.Service);
await EnterBusinessAsync(ct);

// ExitAsync 内：这里只示意顺序，具体字段由项目持有。
_binding?.BeginStopping();
await StopBusinessAsync();
await _uiModule.DisposeAsync();
await _scope.DisposeAsync();
_binding?.Dispose();
_binding = null;
// 返回 Boot 后，由 Boot 清理配置、场景和基础资源服务。
```

`Binding.Dispose()` 仅解绑，不关闭服务。不得用它替代模块清理。中途清理失败时保留 Binding 和模块引用，抛回 Boot；此时入口保持停止状态，新启动无法覆盖，等待再次停止时补做清理。

同一时间仅允许一个绑定，重复绑定直接报错。解绑幂等；每个 Binding 只操作自己的绑定，旧任务持有的凭据不能清空或停止后一次启动。进入 Play 时通过 SubsystemRegistration 清空静态引用，兼容关闭 Domain Reload。

GameEntry 的停止门禁只阻止重新获取服务，无法撤销调用方已缓存的引用，也不会取消已开始的任务。业务必须先取消并等待自己的工作，模块继续遵守各自的关闭契约。初始化失败由 Boot 调用 ExitAsync 回滚；绑定后进入业务失败也执行相同退出路径。

## 4 运行示例

### UI 示例

打开 [UIIntegration.scene](../../Scenes/UIIntegration.scene)，沿用 [25](25-UI最小流程与接入.md) 的 Play 测试步骤。`UiFlowSample` 在 Scope 启动成功后绑定资源与 UI，打开面板使用 `GameEntry.UI`，停止时先关门禁，再等待操作和模块退出，最后解绑。

### 资源示例

`AssetLoadDemo` 已由独立 Start 示例改为 Boot 项目入口。把该组件赋给 BootFlow 的 **Project Entry** 后运行；仅挂在空对象上不会自动创建或初始化资源系统。进入时通过 `GameEntry.Assets` 加载 Probe Prefab，停止时等待实例销毁再归还租约并解绑。

一个 BootFlow 选择一个项目入口。UI 示例和资源示例是两种入口，不要让它们同时绑定；组合业务时由一个入口统一装配。`GeneratedTextLoadSample` 仍是独立资源键教学示例，不接入 GameEntry，勿与正式 Boot 流程混用。

## 5 后续模块规则

- 框架 UPM 包不引用 Game 或 GameEntry，仍通过构造函数、初始化参数接收接口。
- 项目以后安装音频、本地化或网络模块时，按需要新增强类型入口，并在组合根启动完成后统一绑定。
- 当前 UI 根属于应用级；场景内 UI、战斗、关卡和场景池等短生命周期对象使用对应上下文或显式注入，不直接覆盖全局入口。
- 当前入口表示一套应用服务。需要同时运行两套独立世界或测试环境时，业务使用显式注入，不通过全局入口切换。

## 6 验证

新增 `Game.Tests` 12 项 EditMode 用例，验证入口访问、同实例共享、可选模块、重复绑定、停止门禁、旧所有者隔离、静态重置、Boot 就绪等待与取消、业务失败回滚、清理失败重试和资源示例加载失败解绑。

独立消费工程全量 **343/343 通过**，0 失败、0 跳过。Windows x64 Development Mono Player 验证了实际 UiFlowSample 绑定、真实 Addressables/UI 100 次打开关闭、停止后入口不可访问、重新启动后绑定新的服务，以及原有销毁超时/重试和启动失败回滚。IL2CPP、移动端与主工程手动交互不由本轮结果代替。

原始报告和验证脚本见 [GameEntry 证据归档](../evidence/2026-11-18-gameentry/README.md)。
