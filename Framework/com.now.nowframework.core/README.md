# com.now.nowframework.core

NowFrameWork 的核心层（C01 模块容器）。纯 C# 逻辑，不含任何 Unity 表现类型。

## 引用方式

```json
"com.now.nowframework.core": "file:../../Framework/com.now.nowframework.core"
```

> `file:` 的相对路径是**从工程的 `Packages` 目录**算的，不是从工程根。
> 本包在仓库根 `Framework/` 下，工程在仓库根的 `NowFrameWork/` 下，所以是 `Packages/../../Framework/...`。

## 已实现

- **C01 模块容器**：显式依赖 `DependsOn`、拓扑排序、缺失/循环校验、部分初始化失败逆序清理、逆序关闭、`Get<T>`/`TryGet<T>`、`Tick`/`LateTick`/`FixedTick` 分发。

- **小型设置存储**：`Storage/FileSettingsStore<T>`，类型化 DTO、显式同步保存、主/备份恢复与 schema 迁移；通过 `ISettingsCodec<T>` 注入编解码/语义校验。仅主线程、单文件单写入者；先 `Load` 再访问 `Value`/`SetValue`/`Save`。损坏不静默重置，新版本不降级覆盖，详见工程文档 19。
- **线程诊断**：Editor/Development 使用 `MainThreadGuard`；纯 Core 宿主启动时须显式 `BindCurrentThread()`，Unity 适配包自动登记。
- **层级状态机**：`NowFrameWork.Core.Fsm` 支持条件和事件转移、退出协调、历史恢复与可重试清理。宿主驱动根状态机。观察器位于独立的 `NowFrameWork.Core.Fsm.Editor` 程序集。

## 约束

1. 本程序集不引用 `UnityEngine`（`noEngineReferences: true`），Core 里出现 Unity 表现类型即视为违约。
2. 依赖唯一第三方包：UniTask。
3. 模块必须在 `Register` 时显式声明依赖，禁止反射扫描自动注册。

## 测试

`Tests/EditMode` 包含模块、事件、池、线程与存储等回归；存储新增 38 项（含参数化用例）。在 Unity 的 Test Runner > EditMode 中运行；
工程 `Packages/manifest.json` 的 `testables` 需要包含 `com.now.nowframework.core`。

FSM 的状态推进与清理回归位于 `Tests/EditMode/StateMachineTests.cs`。观察器的窗口绘制用例需要图形设备，运行时的物理和动画集成用例由主工程的 PlayMode 测试程序集运行。
