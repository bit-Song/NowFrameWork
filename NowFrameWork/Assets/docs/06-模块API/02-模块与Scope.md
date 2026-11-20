# 模块和 Scope API

`ModuleHub` 按依赖顺序启停模块。`Scope` 把模块、池和子作用域组织到同一个生命周期。

## 常用 API

| API | 何时使用 |
|---|---|
| `IAppModule.Id / DependsOn` | 声明模块唯一 ID 及依赖的其他模块 ID。 |
| `InitializeAsync(ct) / StartAsync(ct) / ShutdownAsync(ct)` | 实现初始化、启动和收尾。Hub 按依赖顺序启动，清理按逆序进行。 |
| `ModuleHub.Register(module) / Get<T>() / TryGet<T>(out module)` | 启动之前注册。查询用具体类型或服务接口，多个实现用带列表参数的 `TryGetAll<T>(buffer)`。 |
| `Scope.StartupAsync(ct) / CreateChild(name) / DisposeAsync()` | 启动后才能创建子 Scope。关闭时按子作用域、模块、池的顺序收尾。 |

## 调用示例

```csharp
// scope 和 myModule 是由项目入口持有的字段或对象。
scope.Modules.Register(myModule);
await scope.StartupAsync(ct);
// 在负责退出的异步方法中调用 await scope.DisposeAsync()。
```

## 使用和清理约定

Boot 的基础 Hub 到项目入口时已启动。自有模块可挂在独立 Scope。局部变量里取得的租约由调用者持有。需要异步等待对象销毁的资源池和 UI 模块要等待各自的清理。

[Scope 与资源层](../03-模块设计/13-Scope与资源层设计.md) · [UI Scope 样例](../../Game/UiFlowSample.cs) · [全部模块 API](README.md)
