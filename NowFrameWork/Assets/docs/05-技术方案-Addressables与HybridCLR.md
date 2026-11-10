# 05 技术方案：Addressables 与 HybridCLR

> 上游：[04-里程碑与实现顺序](04-里程碑与实现顺序.md) ｜ 相关：[07-架构决策记录ADR](07-架构决策记录ADR.md)

本文件回答三个问题：**资源怎么管**、**热更怎么发**、**团结 1.8.2 单环境下的工程基线怎么定**。所有数字与 API 以 M0 实测为准，M0 结束后回填"已锁定"版本。

## 1. 资源系统（F01）

### 1.1 设计目标与边界

| 目标 | 做法 |
|---|---|
| 业务不接触 Addressables API | 业务只用 `AssetRef<T>` / `IAssetService` |
| 资源可追溯 | 每次加载记录 address、调用方、引用计数 |
| 泄漏可发现 | 未释放快照 + 场景/流程切换时断言 |
| 加载不卡主线程 | 异步队列 + 并发上限 + 后台加载 |
| 失败可降级 | 重试 → 抛出明确异常 → 上层决定回退 |

不做：不做自有 AssetBundle 层（ADR-019 已否决）、不做资源热重载（Editor 除外）、不做运行时资源编辑器。

### 1.2 三层结构

```
业务代码
   │  AssetRef<T>（可序列化引用，只有 address + package）
   ▼
IAssetService（句柄、引用计数、队列、进度、重试、快照）
   │  内部 Provider 接口
   ▼
Addressables Provider（LoadAssetAsync / LoadSceneAsync / Release / DownloadDependenciesAsync）
```

Provider 可替换是硬性要求：若 M0 决定换 YooAsset，只替换 Provider 与初始化，`IAssetService` 及业务代码零改动（ADR-004）。

### 1.3 加载模型

| 项 | 约定 |
|---|---|
| 并发上限 | 6（可配置，`AssetServiceOptions.MaxConcurrent`） |
| 优先级 | `UI > Scene > Preload > Background`，高优先级先出队 |
| 同步加载 | 仅限启动阶段且资源已预加载；运行时禁止（会卡帧） |
| 进度 | 单请求 `0–1`，批次按"已完成权重 + 当前请求进度"加权 |
| 取消 | `CancellationToken` 透传到 Addressables handle；取消后引用计数必须归零 |
| 重试 | IO 类错误重试 2 次（0.5s / 1.5s）；资源缺失不重试 |
| 同址合并 | 同 address 的并发请求共享同一底层 handle，只发一次 IO |
| 预加载 | `PreloadAsync(refs, progress)`，用于关卡/界面进入前 |

加载状态机：

```
Idle → Queued → Loading → Loaded → (Released)
                   │
                   └─ Failed → Retrying → Loading / Faulted
```

### 1.4 引用计数与生命周期

```
Load(address)  ──▶ 命中缓存：refCount++，返回同一 handle
                  未命中：入队 → 加载 → 缓存(refCount=1)
Release(handle) ──▶ refCount-- → 0 时 Addressables.Release 并移除缓存
```

规则：

1. `AssetHandle<T>` 是**唯一**释放入口；业务不得调用 `Addressables.Release`。
2. 句柄允许重复 `Dispose`，但第二次仅记录警告不重复释放（防止引用计数变负）。
3. 跨流程持有的资源必须显式声明归属：`IFlowState` 的 `OnEnterAsync` 申请、`OnExitAsync` 释放。
4. 不允许把 `AssetHandle<T>` 存进静态字段。

### 1.5 泄漏检测

- Editor：流程切换完成、场景卸载后，`Snapshot()` 若非空则在 Console 输出地址、引用计数、加载堆栈（用 `#if UNITY_EDITOR` 记录 `StackTrace`）。
- Development Build：同上，但只输出汇总与 Top 20，避免日志爆炸。
- Release：仅保留计数，不记录堆栈。
- CI 冒烟：启动 → 主菜单 → 退出，泄漏快照必须为空，否则构建失败。

### 1.6 分组与地址规范

| 组 | 内容 | 打包方式 | 位置 |
|---|---|---|---|
| `BuiltIn` | 加载界面、错误面板、字体、兜底 dll、AOT 补充元数据（**Boot 场景不在此组**，走 Build Settings，见 ADR-020） | Pack Together，随包 | Local |
| `Common` | 通用图集、材质、音频 | Pack Together | Local |
| `UI/<界面>` | 单界面 Prefab + 图集 | Pack Together | Local 或 Remote |
| `Level/<关卡>` | 关卡场景、地形、光照 | Pack Separately | Remote |
| `Localization/<语言>` | 语言表与变体资源 | Pack Together | Remote |
| `HotUpdate` | 热更 dll、`hotfix_manifest.json` | Pack Together | Remote |

地址格式：`{模块}/{类型}/{名称}`，全英文 PascalCase，例如 `UI/MainMenu`、`Level/L01/Scene`、`Audio/BGM/MainTheme`。

规则：

- 地址一旦发布**不得修改**（等于资源 ID）；改名走新增 + 旧地址保留一个版本。
- 禁止两个组存在同一资源的隐式依赖：被 ≥ 2 个组引用的资源必须下沉到 `Common`。
- 变体（画质档）用 Addressables Variant，加载时按 `F07 设置` 选择，业务无感知。

### 1.7 场景加载

```
Addressables.LoadSceneAsync(sceneRef, LoadSceneMode.Additive, activateOnLoad: false)
  → 资源就绪 → 流程切入 → 手动 ActivateSceneAsync
卸载：先释放场景内所有句柄 → UnloadSceneAsync → 再卸资源
```

约束：

- 同一时刻最多 2 个激活场景（主场景 + 叠加场景），超出必须先卸载。
- `Boot` 场景常驻，不卸载。
- 场景切换期间禁止其他流程切换（`IFlowService` 串行锁）。

### 1.8 失败与降级

| 失败类型 | 处理 |
|---|---|
| 网络中断（远端） | 重试 2 次 → 用本地缓存 → 仍失败则提示并留在当前界面 |
| Catalog 不可用 | 回退内置 catalog（首包版本） |
| 资源缺失（Key 不存在） | 抛 `AssetLoadException`，开发期直接红字报错；发布期用占位资源 |
| 下载校验失败 | 删除坏包 → 重新下载 → 仍失败回退上一版本目录 |

### 1.9 包体治理

- 单个 bundle ≤ 64 MB（PC 建议值）；超限拆分。
- 首包资源 ≤ 项目预算的 30%；框架自身资源 ≤ 20 MB。
- 每次出包由 T01 输出报告：bundle 数、总大小、重复依赖 Top 20、未使用资源清单。
- 允许的重复依赖上限：0（同名资源出现在两个 bundle 即视为缺陷，需下沉 Common）。

## 2. HybridCLR 热更（T02 / F02）

### 2.1 程序集划分

| 分类 | 程序集 | 说明 |
|---|---|---|
| AOT | `NowFrameWork.Core`、`NowFrameWork.Foundation`、`NowFrameWork.Gameplay`、`Game.Launcher` | 随包，不可热更 |
| 热更（PC） | `Game.Logic`、`Game.UI`、`Game.Data` | 由 Addressables 分发 |
| 主机 | 同上三个，但以 AOT 形式随包，由 `Game.Entry.Console` 引用 | 见 2.7 |

硬约束：AOT 程序集不得引用热更程序集；热更程序集可以引用 AOT 程序集。跨层通信只走 AOT 侧接口（`IGameEntry`、事件、`IAssetService` 等）。

### 2.2 构建流水线

标准流程（M0 先跑通这一条）：

| 步骤 | 命令/动作 | 产物 |
|---|---|---|
| 1 | `HybridCLR/Generate/All` | 桥接函数、link.xml、`AOTGenericReferences`、裁剪后 AOT dll |
| 2 | 编译热更程序集 | `Library/HybridCLR/HotUpdateDlls/{platform}/Game.*.dll` |
| 3 | 收集补充元数据 | `Library/HybridCLR/AssembliesPostIl2CppStrip/{platform}/*.dll` |
| 4 | 拷贝为 `.bytes` 至 `Assets/_Project/HotUpdateRes/`，生成 `hotfix_manifest.json` | 热更资源输入 |
| 5 | Addressables 构建 `HotUpdate` 组 | 远端目录 |
| 6 | Build Player（IL2CPP） | 热更程序集被构建钩子自动排除 |
| 7 | 生成发布清单并归档 | 包、补丁、符号、报告 |

优化流程（M4 后再做，用于缩短一半出包时间）：`Generate/LinkXml` → 导出工程 → `Generate/Il2cppDef` → `Generate/MethodBridgeAndReversePInvokeWrapper` → 替换 `generated` 目录 → 在导出工程上 build。Unity 2019 Android 与团结引擎鸿蒙平台不支持该流程。

### 2.3 运行时加载顺序

```
1. Addressables.InitializeAsync
2. 读本地 hotfix_manifest.json 与远端比对 → 下载补丁 → sha256 校验
3. 逐个加载 AOT 补充元数据 dll（.bytes）
   → RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.Consistent)
4. 按依赖拓扑序加载热更 dll（后台线程执行 Assembly.Load，减少主线程卡顿）
5. 反射找到 IGameEntry 实现 → 调用 Initialize()
6. 释放所有 dll 的 AssetHandle（bytes 不保留）
```

顺序不可交换：元数据必须在热更 dll 之前；多 dll 必须按依赖序（被依赖的先加载）。

内存注意：`Assembly.Load` 内部会复制一份 bytes，调用后立即释放原数组与句柄，否则内存翻倍。

### 2.4 入口约定

AOT 侧定义唯一入口接口：

```csharp
public interface IGameEntry
{
    UniTask InitializeAsync(CancellationToken ct);
    void Tick(float deltaTime);
    void Shutdown();
}
```

热更层实现固定全名：`Game.HotUpdate.Entry.GameEntry`。Launcher 只做反射查找与调用，且查找失败时走兜底 dll / 安全模式。推荐用"热更入口 Prefab + AddComponent"方式初始化（HybridCLR 官方推荐），反射只用于一次入口获取。

### 2.5 版本、校验与回滚

| 版本 | 含义 | 写入方 |
|---|---|---|
| `AppVersion` | 安装包版本 | 构建脚本 |
| `ResourceVersion` | Addressables catalog 版本 | Addressables 构建 |
| `HotfixVersion` | 热更 dll 集版本 | 热更构建 |

兼容规则：

- 热更包声明 `minAppVersion`，不匹配则拒绝加载并使用内置 dll。
- 远端保留最近 3 个 HotfixVersion 目录；回滚只需把清单指回上一版。
- 客户端提供启动参数 `--safe-mode`：跳过热更、只用内置 dll，用于线上急救。
- 补丁下载失败、校验失败、dll 加载异常，三处都回退到内置兜底 dll（ADR-007）。

### 2.6 限制与常见坑

| 坑 | 表现 | 处理 |
|---|---|---|
| AOT 泛型缺失 | `ExecutionEngineException` / 泛型方法返回异常 | `AOTGenericReferences` + 补充元数据；开发期故意制造一次验证排查手段 |
| 代码裁剪 | 反射调用的类型/方法被裁 | `Managed Stripping Level = Minimal` + link.xml + 保留清单 |
| AOT 引用热更 | 打包阶段直接报错 | 程序集划分与 CI 边界检查（T01） |
| 多 dll 顺序 | `TypeLoadException` | 拓扑排序加载，清单里显式写依赖顺序 |
| bytes 未释放 | 内存翻倍 | 加载后立即释放数组与句柄 |
| 热更程序集被编进 AOT | 包里多一份 `Game.Logic` 的 AOT 副本，热更永远不会生效 | M0 必须实测"Player 包内确实不含热更程序集"，不能只依赖"构建钩子自动排除" |
| MonoBehaviour 热更脚本 | 场景里拖拽的脚本丢失 | 用 `AddComponent(Type)` 或热更入口 Prefab |
| Editor 直接运行 | 误以为没热更 | Editor 走 Mono，热更代码本来就直接运行，构建后才验证加载链路 |
| 符号 | Release 包无法看堆栈 | Debug 构建附带 pdb；Release 走映射文件还原 |

### 2.7 主机策略（条件项）

> 仅在主机立项时启用。团结引擎当前不提供 PS5 / Xbox 构建目标，一旦立项需**整体迁移**到 Unity 2022.3.67f2（GUID/场景格式转换 + 重烘 + 重打 AB，预估 5–8 人日，ADR-017）；不立项则本节不产生任何构建成本。

主机（PS5 / Xbox Series）**不支持动态加载代码**，因此：

- `IPlatform.SupportsHotUpdate = false`；
- 构建变体将 `HotUpdateAssemblies` 置空，同一份 `Game.Logic/UI/Data` 作为 AOT 程序集编入包；
- `Game.Entry.Console`（asmdef `includePlatforms` 仅主机）直接引用并启动业务入口；
- 主机补丁 = 整包更新，走平台审核；框架侧保证"同一份代码两套产物"，不做业务分支。

风险提示：主机侧"随包业务程序集"与"热更程序集"的源码完全一致，但编译产物不同（AOT vs 解释执行），因此**主机需单独跑一次全量冒烟**，不能只验 PC。

### 2.8 调试

- Editor：直接运行热更代码；`HybridCLR/Generate/All` 后可用 `HybridCLR/Build/BuildAndCopy` 快速验证。
- 包内：Debug 构建加载 pdb，堆栈可读；Release 保留 mapping，崩溃后离线还原。
- 日志：`hotfix_manifest.json` 的版本、dll 哈希、加载耗时全部进日志，便于线上定位。

## 3. 引擎环境（团结单环境）

**首版只有团结引擎 1.8.2 一个环境**：开发、出包、CI 全用它，不存在"另一个引擎能不能打开"的问题（ADR-017 v0.3）。下表的作用是为将来迁移少返工，而不是维持双引擎并行。

| 项 | 结论 | 处理 |
|---|---|---|
| 引擎版本 | 团结 1.8.2 = `2022.3.62t4`（1.6.6 = `2022.3.61t7`），同属 2022.3 LTS | 只用 2022.3 公共 API；团结小版本升级后重跑 M0 冒烟 |
| **工程资产格式** | 团结写自己的格式：YAML 头 `%TAG !u! tag:yousandi.cn,2023:`、场景后缀 `.scene`、新建资源的 `.meta` GUID 为 56 字符 base64 | **Unity 无法直接打开**。迁移要做三件事：GUID 转 32 位十六进制 → `.scene` 改 `.unity` → 修 AssetDatabase 层级，然后重烘场景、重打 AB。因此禁止把"格式转换"当日常流程 |
| Addressables | **1.22.4-t1**（团结 t 版 = 1.22.3 + OpenHarmony 打包修复），**不随编辑器安装**（团结内置包里没有），需在 manifest 显式添加 | 锁 1.22.4-t1；不升 2.x/3.x（那是 Unity 6 之后的分支）；`packages-lock.json` 入库 |
| HybridCLR | 社区版自 **5.2.0** 起支持团结；当前主线 9.1.0，上一稳定线 8.15.0 | M0 先试 9.1.0，不行退 8.15.0；必须实测安装器认不认 `2022.3.62t4` |
| 小游戏 metadata | 团结 1.2.0+ 的 `Use Slim Format For global-metadata.dat` 会导致 HybridCLR 失效 | 若发小游戏，Publishing Settings 必须关闭该选项 |
| 主机平台 | 团结平台列表无 PS5 / Xbox / Switch | 首版不做；立项即迁移到 Unity 2022.3.67f2，预估 5–8 人日（ADR-017） |
| 构建入口 | 团结为 `Tuanjie.exe`，命令行参数与 `Unity.exe` 一致 | CI / 出包脚本用 `-EnginePath` 参数化，默认团结，迁移时只改参数 |
| 团结独有包 | `cn.tuanjie.*`（AI Graph、Infinity、URAS 等）与团结独有模块（instantasset、autostreaming 等） | 禁止进入框架层；业务层按需引入，且不得反向污染 Core/Foundation |

## 4. 项目配置基线

M0 时逐项确认并写入 `docs` 附表；任何变更需在 07 中记录。

| 设置 | 值 | 原因 |
|---|---|---|
| 引擎 | 团结 1.8.2（2022.3.62t4） | 唯一环境（ADR-017） |
| Scripting Backend | IL2CPP | AOT 与性能前提 |
| Api Compatibility Level | .NET Framework | HybridCLR 推荐，BCL 更完整 |
| Managed Stripping Level | Minimal | 防 AOT 泛型与反射被裁 |
| Il2Cpp Code Generation | Faster (runtime) | PC 性能优先 |
| Incremental GC | 开 | 降低卡顿 |
| Active Input Handling | Input System (New) | F08 |
| Color Space | Linear | 现代渲染 |
| Graphics API（Win） | DX11 优先 | 稳定性优先，DX12 单独测试 |
| Addressables Build Remote Catalog | 开 | 支持更新 |
| Bundle Mode | Pack Together（默认） | 控制 bundle 数量 |
| Compression | LZ4 | 加载速度与体积平衡 |
| 渲染管线（示例工程） | URP | 跨平台友好；框架代码不依赖具体管线 |


## 5. 性能预算与监控

| 监控点 | 指标 | 埋点 | 告警阈值 |
|---|---|---|---|
| 启动 | 冷启动到首帧 | `PerfScope("Boot")` | > 1.5s |
| 启动 | 到主菜单可交互 | `PerfScope("Boot.ToMenu")` | > 5s |
| 资源 | 单次加载耗时 | `PerfScope("Asset.Load")` + address | > 500ms |
| 场景 | 切换总耗时 | `PerfScope("Scene.Switch")` | > 3s |
| UI | 打开耗时 | `PerfScope("UI.Open")` + 界面名 | 首开 > 200ms / 缓存 > 50ms |
| 内存 | 托管堆常驻 | 每 30s 采样 | > 预算 20% |
| 帧 | 框架层耗时 | `PerfScope("Frame.Framework")` | > 1.0ms |

## 6. 风险登记

| ID | 风险 | 概率 | 影响 | 缓解 | 验证 | 预案 |
|---|---|---|---|---|---|---|
| R1 | Addressables × HybridCLR 集成受阻 | 中 | 高 | M0 优先验证，不写业务代码 | M0 | 换 YooAsset（Provider 隔离） |
| R2 | IL2CPP 裁剪丢 AOT 泛型 | 高 | 高 | Minimal + link.xml + 泛型引用生成 | M0 冒烟 | 保留清单逐类型补 |
| R3 | 主机不允许动态代码 | 确定 | 高 | 双模式 AOT 入口 | M4 | 主机只出整包 |
| R4 | 资源重复依赖导致包体膨胀 | 高 | 中 | 分组规范 + 分析工具 + 重复依赖为 0 | 每次出包 | 下沉 Common |
| R5 | UI Canvas 重建掉帧 | 中 | 中 | Canvas 拆分 + 图集 + 列表虚拟化 | M3 压测 | 拆分层级 |
| R6 | 框架过度设计拖慢业务 | 中 | 中 | P0/P1/P2 分级 + 垂直切片反推 | M5 | 砍 P2 |
| R7 | 存档不兼容导致进度丢失 | 中 | 高 | Schema 版本 + 迁移测试 + 备份 | M3 | 兼容模式读旧档 |
| R8 | 补丁损坏导致无法启动 | 中 | 高 | 内置兜底 dll + safe-mode | M4 | 回滚上一版本 |
| R9 | 依赖升级破坏热更 | 中 | 中 | 版本矩阵锁定 + 升级必冒烟 | 每次升级 | 回退版本 |
| R10 | 出包时间过长影响迭代 | 中 | 低 | 增量构建 + 资源分包 | M4 | 资源与代码分离构建 |
| R11 | 只在一个引擎上验证，Unity 生态方案换算成本高 | 中 | 低 | 只用 2022.3 公共 API；遇到"Unity 专有"方案先评估替代成本 | 全程 | 按 2022.3 文档找等价 API |
| R12 | 团结小版本升级破坏热更/资源链路 | 中 | 高 | 版本矩阵锁定；升级必须重跑 M0 冒烟 | 升级时 | 回退旧版本 |
| R13 | 主机需求后置导致架构假设失效 | 中 | 高 | 双模式设计保留但按条件实现；主机立项即整体迁移（ADR-017） | 立项时 | 只做 PC 入口 |
| R14 | 用 Addressables Variant 做语言/画质变体 | 中 | 中 | 首版语言资源改"分组 + label"，不用 Variant；画质档再评估 | M3 | 改用 label 选择 |
| R15 | 主机立项时工程格式迁移超预期 | 中 | 高 | 提前记录格式差异（§3）；立项时先在副本上用转换工具演练一次 | 立项时 | 迁移预算按 5–8 人日上浮 50% |
