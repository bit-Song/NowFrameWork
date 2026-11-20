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

在 `BootIntegration.scene` 的 Game 窗口观察 `Running`，点击「取消 / 停止」后等清理完成。入口退出失败时保留清理所需字段，后续 Stop 重试。帧驱动计时器应在 Running 后等待。平台信息与暂停、恢复等信号分别从 `context.App.Platform` 和 `context.App.Lifecycle` 获取。

[Boot 启动与停止](../04-测试与接入/21-Boot启动整合.md) · [GameEntry 接入](../04-测试与接入/28-GameEntry服务入口与生命周期.md) · [全部模块 API](README.md)
