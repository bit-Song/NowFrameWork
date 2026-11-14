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

## 约束

1. 本程序集不引用 `UnityEngine`（`noEngineReferences: true`），Core 里出现 Unity 表现类型即视为违约。
2. 依赖唯一第三方包：UniTask。
3. 模块必须在 `Register` 时显式声明依赖，禁止反射扫描自动注册。

## 测试

`Tests/EditMode/ModuleHubTests.cs`，9 个用例。在 Unity 的 Test Runner > EditMode 中运行；
工程 `Packages/manifest.json` 的 `testables` 需要包含 `com.now.nowframework.core`。
