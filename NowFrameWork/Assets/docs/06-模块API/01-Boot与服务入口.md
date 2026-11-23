# Boot 与服务入口 API

`BootFlow` 是场景组件，`BootSession` 管理一次启动和清理，`IBootEntry` 是接入自有模块的位置。装配在 `EnterAsync()` 中使用 `BootContext`。已绑定服务可通过 `GameEntry` 访问。

## 常用 API

| API                                                | 何时使用                                                                              |
| -------------------------------------------------- | --------------------------------------------------------------------------------- |
| `BootFlow.Run() / Stop()`                          | 从场景代码发起启动或停止。组件把失败写到 Console，验收时看阶段和最终停止日志。                                       |
| `BootSession.StartAsync(ct) / StopAsync()`         | 供持有会话的宿主调用，异步结果直接报告异常。用 `State`、`FailureStage`、`LastError` 定位失败。                  |
| `IBootEntry.EnterAsync(context, ct) / ExitAsync()` | 由 Boot 调用。入口在这里启动自有模块，在退出时等待其任务、实例和租约清理。                                          |
| `GameEntry.Bind(context, ui)`                      | 由入口在依赖就绪后绑定。返回的 `Binding` 由入口保存。先 `BeginStopping()` 关闭访问，再完成清理并调用 `Dispose()` 解绑。 |
| `GameEntry.IsReady / HasUI / HasScenes`            | 先检查就绪，再判断 UI 和场景服务是否已装配。已取得的服务引用也应遵循自己的生命周期。                                      |

## 调用示例

```csharp
// 接入代码在入口绑定完成后读取配置。
if (!GameEntry.IsReady) return;
var config = await GameEntry.Configs.LoadAsync<TextAsset>("PCProbe/Config", ct);
Debug.Log(config.text);
```

## 使用和清理约定

进入 Play 或构建前，可在团结编辑器运行 **NowFrameWork → 检查 → 预检当前场景**，也可以一次检查 **启用的构建场景**。预检检查项目入口和代码加载器的接口、发布模式选项、本地 Addressables 的配置及场景地址，以及框架 UI 样例的根 Prefab、EventSystem 和面板 `UiView`。

Console 报错包含检查编号、场景路径和层级位置。点击打开场景的报错可定位对象，未打开的场景会定位到场景资产。检查读取场景和本地 Addressables 设置，运行时远端 Catalog 与动态装配仍由运行验收覆盖。

修改 UPM 依赖或 `.asmdef` 后，可运行 **NowFrameWork → 检查 → 预检框架包与程序集**。检查以已安装的 NowFrameWork 包为起点，解析 `references` 中的程序集名称和 GUID：

- `DEP001` 表示框架程序集反向引用了项目程序集。
- `DEP002` 表示跨包引用了程序集，但当前包的 `package.json` 缺少直接依赖声明。
- `DEP003` 表示程序集引用缺失或存在歧义，`DEP004` 表示无法读取框架程序集定义。

报错可点击定位到发出引用的 `.asmdef`。测试程序集使用宿主 Unity Test Framework 提供的两个 TestRunner 程序集，普通跨包引用仍需显式声明。预编译 DLL 和动态加载关系由独立消费工程或运行验收覆盖。

需要同时执行构建场景预检、包依赖预检与 EditMode 回归时，可使用 [本地一键回归](../05-进度与评审/15-进度与下一步.md#t03-本地一键回归)。

在 `BootIntegration.scene` 的 Game 窗口观察 `Running`，点击「取消 / 停止」后等清理完成。入口退出失败时保留清理所需字段，后续 Stop 重试。帧驱动计时器应在 Running 后等待。平台信息与暂停、恢复等信号分别从 `context.App.Platform` 和 `context.App.Lifecycle` 获取。

[Boot 启动与停止](../04-测试与接入/21-Boot启动整合.md) · [GameEntry 接入](../04-测试与接入/28-GameEntry服务入口与生命周期.md) · [全部模块 API](README.md)
