# 13 Scope 与资源层设计

> 对应缺口 **G-05**（[12](12-框架缺口清单.md)）。本文只定契约与时序，不含实现；实现排在资源里程碑。
> 已定前提：[05 §2 租约语义](05-技术方案-Addressables与HybridCLR.md)、[ADR-004](07-架构决策记录ADR.md)（封装 Addressables）、[ADR-025](07-架构决策记录ADR.md)（独立租约与共享加载取消）、[11](11-对象池与资源租约.md)（池级保活租约）。

> **评审状态（2026-11-15）**：已评审。发现 3 处硬伤（§4 `Dispose` 签名、§4.2 跨 Scope 解析、§8.3 与 §1 策略冲突）与 5 处契约缺件。全部结论、修订后的契约与待补用例见 [§9](#9-评审意见2026-11-15)；§2 / §4 的契约建议直接按 §9.6 替换。

## 1 要解决什么

| 问题 | 现状 | 本文的答案 |
|---|---|---|
| 场景级服务无处安放 | 所有模块都是全局单例 | `Scope` 树：每个 Scope 拥有自己的模块与池 |
| 切场景无法逆序释放 | 只能靠手写 | Scope 关闭顺序固定：模块 → 池 → 租约 |
| 资源何时能卸载说不清 | 只有租约计数，没有归属 | 租约归属到 Scope，Scope 关闭才允许卸载 |
| 池里残留已卸载资源的实例 | 无约束 | 池的保活租约挂在 Scope 上，Scope 先清池再放租约 |

## 2 资源层契约（草案）

```csharp
public interface IAssetService
{
    // 申请一个独立租约；同址并发请求共享底层加载，但各自拿到租约
    UniTask<AssetLease<T>> AcquireAsync<T>(string address, CancellationToken ct) where T : UnityEngine.Object;

    // 缓存保留：不参与业务引用计数，用于池/常驻资源保活
    UniTask<AssetLease<UnityEngine.Object>> AcquireCacheAsync(string address, CancellationToken ct);

    // 诊断
    int PendingRequests { get; }
    int ActiveLeases { get; }
}

public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
{
    public T Asset { get; }
    public string Address { get; }
    public bool IsReleased { get; }
    public void Dispose();   // 幂等；计数归零且无人等待时才允许卸载
}
```

三个必须遵守的语义（来自 [05 §2](05-技术方案-Addressables与HybridCLR.md)）：

1. **同址双请求 = 两个租约**：释放 A 不使 B 失效，重复释放 A 不改变 B 的计数；
2. **取消只结束该等待者**：其余等待者继续；即使底层操作最终完成，也不交付给已取消者；
3. **失败也要回收底层句柄**：不允许"加载失败但句柄泄漏"。

> ⚠️ **评审（§2）**
> ① `AcquireCacheAsync` 返回 `AssetLease<UnityEngine.Object>` **丢失类型**——池要 `Instantiate` prefab，拿到 `Object` 每次都得 cast，cast 失败只能运行时发现。建议泛型化。
> ② 只接受 `string address`，**`AssetReference` 用不了**——SO 配置里是 `AssetReferenceGameObject` 时，调用处得先取 `.RuntimeKey` 拼字符串，把类型安全又丢了。建议改为 `object key`。
> ③ **缺 `InitializeAsync` 与整条热更链路**（`CheckForUpdates` / `UpdateCatalogs` / `Download` / `GetDownloadSize`）。不补，业务只剩两条路：违反 §3 硬规则直接调 Addressables，或者热更永远没人写。
> ④ `AssetLease` 释放后 `Asset` 仍返回旧引用，建议 getter 在 `IsReleased` 时抛异常（开发期），否则"释放后继续用"极难排查。
> 详见 §9.3 / §9.4，修订版见 §9.6。

## 3 Provider 边界

```csharp
public interface IAssetProvider
{
    UniTask<ProviderHandle> LoadAsync(string address, CancellationToken ct);
    void Release(ProviderHandle handle);
    UniTask<long> GetDownloadSizeAsync(string address);
}
```

- `AddressablesProvider` 是首个实现；业务与框架其它模块**只依赖 `IAssetService`**，不直接调 Addressables（[06 §2](06-工程规范-测试与验收.md) 已有硬规则）；
- 换 Provider（YooAsset、裸 AB）只替换这一层，不改 Scope 与池。

> ⚠️ **评审（§3）**：`ProviderHandle` 未定义——是 class 还是 struct、能否重复 `Release`、谁负责回收，契约缺这层定义就无法实现。另 `Release` 是同步方法，而 Addressables 卸载 bundle 是异步的，需要说明"调用 `Release` 到资源真正卸载之间"的语义（这段时间内存尚未回落）。

## 4 Scope

```csharp
public sealed class Scope : IDisposable
{
    public string Name { get; }
    public Scope Parent { get; }
    public ModuleHub Modules { get; }      // 每个 Scope 一个容器
    public PoolService Pools { get; }      // 池归属该 Scope

    public Scope CreateChild(string name);
    public UniTask StartupAsync(CancellationToken ct, IStartupProgress progress = null);
    public void Dispose();                 // 逆序：子 Scope → 模块 → 池 → 租约
}
```

> ⚠️ **评审（§4，硬伤）**
> **`Dispose()` 返回 `void`，但 §4.1 第 2 步要等 `ShutdownAsync()`** —— 异步关闭无法在同步 `Dispose` 里正确完成：`.Wait()` 会阻塞主线程，fire-and-forget 则关闭还没结束就返回。应改为 `UniTask DisposeAsync(bool purgeAssets = false)`，`IDisposable` 只留同步兜底（断言 + 记 Error）。
> **另缺状态机**：重复 `Dispose()` 是否幂等、`StartupAsync` 进行中被 `Dispose` 会怎样、`StartupAsync` 之后还能否 `Modules.Register`（应当冻结）。这三条不定，Scope 就是个不设防的容器。修订版见 §9.6。

### 4.1 关闭顺序（唯一正确顺序）

```
1. 子 Scope.Dispose()          ← 先关子级，避免父级释放后子级还在用
2. 模块 ShutdownAsync()       ← 业务停止使用资源
3. 池 Dispose(force: true)     ← 销毁实例，归还池级保活租约
4. 其余租约 Dispose()          ← 计数归零
5. 资源层允许卸载             ← 由租约计数驱动，不是 Scope 主动调
```

**为什么池必须在租约之前**：池持有保活租约，若先放租约再清池，池在销毁实例期间资源可能已被卸载。

### 4.2 与现有容器/池的关系

| 现有组件 | 变化 |
|---|---|
| `ModuleHub` | 不变；每个 Scope 各持一个实例（根 Scope 承载全局服务） |
| `PoolService` | 不变；由 Scope 持有，Scope 关闭时 `Dispose()` |
| `ObjectPool<T>` | 不变；资源相关部分由 `AssetPool` 组合 |
| `App` | 增加 `RootScope`，`Hub` 改为 `RootScope.Modules` 的转发（保持兼容） |

> ⚠️ **评审（§4.2，硬伤）**：既然"每个 Scope 各持一个 `ModuleHub`、隔离干净"，就必须回答一个问题：**场景 Scope 里的模块怎么拿到根 Scope 的全局服务**（`IAudioService` / `ILogService` / `IInputService`）？三选一并写死：
> ① 子 Scope 创建时把全局服务显式再注册一遍（实现简单，但两套 Hub 指向同一批实例）；
> ② `ModuleHub` 支持 parent 链查找（先查自己 → 再查父 → 到根为止）；
> ③ 全部构造注入，禁止查 Hub（最干净，但与现有 Hub 并存会显得不一致）。
> 不定这条，本表里"`ModuleHub` 不变"就成立不了——② 是要改 `ModuleHub` 的。

## 5 关键时序

### 5.1 进入场景

```
scope = root.CreateChild("Level01")
await scope.StartupAsync(ct, progress)
  → 模块 Initialize（其中 AssetPool 预热：AcquireCacheAsync + 预创建实例）
  → 模块 Start
```

### 5.2 切场景

```
newScope = root.CreateChild("Level02")
await newScope.StartupAsync(ct)        // 新场景加载完成
oldScope.Dispose()                     // 逆序释放旧场景
```

先加载后释放：避免"黑屏期"，代价是峰值内存高。低内存设备可改为先释放后加载——这是策略，不是框架强制。

> ⚠️ **评审（§5.2）**：**缺失败回滚**。`await newScope.StartupAsync(ct)` 抛异常时，`oldScope.Dispose()` 根本不会执行 → 旧场景存续、新 Scope 泄漏、玩家卡在两个状态之间。需明确：新 Scope 启动**成功**才关旧的；**失败**则保留旧的、逆序清理新的。补用例 S-03。

> 另注：两个 Scope 同时存在期间，若共享同一张图集，底层加载靠"同址共享"只做一份，但**池是每个 Scope 一份**，各自预热会各建一批实例——这部分实例内存不会共享，切换时要注意峰值。

### 5.3 运行时借用

```
pool.Rent()            → 命中闲置：同步返回
pool.RentAsync(ct)     → 未命中：名额检查 → 异步创建
```

资源是否还在内存由池的保活租约保证，`RentAsync` 不触发加载。

> ⚠️ **评审（§5.3）**
> ① **`Rent()` 未命中时干什么没写**：抛异常 / 返回 null / 内部同步创建？三者对调用方完全不同，必须明确（建议抛异常，强制走 `RentAsync`，避免同步创建造成卡顿且无进度）。
> ② 需明确写一句"**`Rent` 不产生新租约**"——否则每颗子弹一次 `AssetLease` 分配，与零分配目标冲突。
> ③ §5 全节没提**场景本身怎么加载**：用 `Addressables.LoadSceneAsync` 还是 `SceneManager`？场景 bundle 的租约占谁的账（Scope 还是资源层）？这是 Scope 的核心用例，目前是空的。

## 6 失败矩阵

| 情况 | 必须的行为 |
|---|---|
| 资源加载失败 | 租约不产生；等待者收到异常；底层句柄回收 |
| 等待中取消 | 只移除该等待者；其余继续；不产生租约 |
| 全部等待者取消 | 按 Provider 能力取消底层操作，或登记完成后立即释放 |
| Scope 启动失败 | 逆序清理：池 → 已启动模块 → 子 Scope |
| Scope 关闭中仍有对象借用 | 池 `Dispose(force: true)` 强杀，记 Error（通过 ErrorBudget 聚合） |
| 资源已卸载仍被使用 | **不该发生**（保活租约挡住）；若发生则由池的 `isValid` 兜底并记 Error |
| 同帧销毁实例并释放租约 | **禁止**（见 §10 实测）：`Object.Destroy` 延迟生效，而 `Unload(true)` 立即失效 → 必须跨一帧再释放租约 |

## 7 验收用例（实现时按此写）

| 编号 | 场景 | 断言 |
|---|---|---|
| A-01 | 同址并发两次 Acquire | 两个不同租约；释放其一另一个仍可用 |
| A-02 | 重复释放同一租约 | 幂等，不改变其它租约计数 |
| A-03 | 等待中取消 | 只有该等待者失败，其它成功 |
| A-04 | 加载失败 | 不产生租约，计数归零 |
| S-01 | Scope 关闭 | 顺序为 子 → 模块 → 池 → 租约，且池销毁后租约才归零 |
| S-02 | Scope 启动失败 | 逆序清理，无残留池与租约 |
| P-01 | 池持有保活租约时卸载请求 | 资源不会被卸载 |
| P-02 | 池 Dispose 后 | 租约计数归零，资源可卸载 |
| **P-05** | 销毁实例与释放租约的时序 | 释放租约前至少跨一帧；期间对象仍可安全访问 |

## 8 待拍板

| # | 问题 | 建议 |
|---|---|---|
| 1 | Scope 是否各持一个 `ModuleHub` | **是**。隔离干净；根 Scope 承载全局服务，切换场景互不影响 |
| 2 | 池键是否强制 `asset:<地址>` 前缀 | **是**。便于"按资源键整池释放"，也便于诊断 |
| 3 | 租约归零后立即卸载还是延迟 | **延迟**（缓存保留 + 可配置淘汰），由资源层策略控制；Scope 只负责归还 |
| 4 | UI/音频是否纳入 Scope | **分层**：UI 根与音频服务在根 Scope 常驻；场景级 UI（HUD）随场景 Scope |

> ⚠️ **评审（§8，硬伤）**
> **第 3 条与 §1 冲突**：§1 承诺"Scope 关闭才允许卸载"，§8.3 又说"租约归零后延迟卸载（缓存保留）"。两者叠加的实际结果是——**切完场景，旧场景资源仍缓存在内存里**，玩家体感就是"内存不回落"。建议拆成两条策略：租约归零 → 缓存保留（LRU + 上限）；`Scope.DisposeAsync(purgeAssets: true)` → 强制卸载本 Scope 独占的资源。
> **第 2 条**：池键 `asset:<地址>` 意味着同一 prefab 只能有一个池。需要两套不同配置（不同父节点 / 不同初始状态）时就不够用了，建议键做成 `(地址, 变体)`。
> **第 4 条**：HUD 若正在播淡出动画时被强杀，观感很差，需要"播完再 Dispose"的能力。

---

## 9 评审意见（2026-11-15）

> 结论：契约骨架成立，租约语义与关闭顺序的思路是对的。问题集中在三处——**异步关闭的签名、跨 Scope 的解析规则、缓存保留与 Scope 卸载的策略冲突**。前两处决定 `ModuleHub` 要不要改，第三处决定"切场景内存能不能回落"。

### 9.1 硬伤（改签名 / 补规则，越晚越贵）

| 编号 | 位置 | 问题 | 处理 |
|---|---|---|---|
| H-01 | §4 `Scope.Dispose()` | 返回 `void`，但 §4.1 第 2 步要等 `ShutdownAsync()`。`.Wait()` 阻塞主线程；fire-and-forget 会让关闭提前返回 | 改 `UniTask DisposeAsync(bool purgeAssets = false)`；`IDisposable` 仅同步兜底 |
| H-02 | §4.2 | 未定义场景 Scope 如何取得根 Scope 的全局服务 | 定死一种解析规则（§9.6 建议 parent 链） |
| H-03 | §8.3 vs §1 | "延迟卸载"与"Scope 关闭才允许卸载"叠加 → 切场景后旧资源仍占内存 | 拆为"缓存保留"与"`purgeAssets` 强制卸载"两条策略 |

### 9.2 契约缺件（不补就会被绕过）

| 编号 | 缺什么 | 后果 |
|---|---|---|
| M-01 | 热更链路：`CheckForUpdatesAsync` / `UpdateCatalogsAsync` / `DownloadAsync` / `GetDownloadSizeAsync` | 业务被迫直接调 Addressables，**违反 §3 自己的硬规则** |
| M-02 | `InitializeAsync(ct)` | Addressables 读 catalog 的时机没有归属；初始化顺序无法排进拓扑 |
| M-03 | `AcquireCacheAsync` 丢失 `T` | 池实例化要 cast，失败只能运行时暴露 |
| M-04 | 只接受 `string address` | `AssetReference` 用不了，SO 配置的类型安全被抹平 |
| M-05 | `ProviderHandle` 未定义 | class/struct、幂等性、回收责任都没写，Provider 无法实现 |

### 9.3 语义漏洞（实现时会咬人）

| 编号 | 位置 | 问题 |
|---|---|---|
| G-01 | §5.2 | 缺失败回滚：新 Scope 启动抛异常时旧 Scope 不会被关，且新 Scope 泄漏 |
| G-02 | §5.3 | `Rent()` 未命中行为未定义（抛异常 / null / 同步创建） |
| G-03 | §4 | 缺状态机：重复 `Dispose`、`StartupAsync` 中 `Dispose`、`StartupAsync` 后 `Register` |
| G-04 | §5 | 未提场景本身如何加载，场景 bundle 的租约归属不明 |
| G-05 | §2 | 释放后 `Asset` 仍返回旧引用，建议 getter 抛异常 |
| G-06 | §6 | `isValid` 的定义与检查点未写；资源卸载后访问引用本身即可能拿到 fake-null |
| G-07 | §8.2 | 池键单变体，同 prefab 多套配置的场景覆盖不到 |
| G-08 | §5.1 | 预热部分失败时，已建实例与已申请租约的回滚未定义 |

### 9.4 建议补充的验收用例

| 编号 | 场景 | 断言 |
|---|---|---|
| S-03 | 切场景时新 Scope 启动失败 | 旧 Scope 完好可继续用；新 Scope 无残留池与租约 |
| S-04 | `Dispose` 中调 `CreateChild`；`StartupAsync` 中调 `Dispose` | 明确抛错，不产生半关闭状态 |
| S-05 | `StartupAsync` 完成后 `Modules.Register` | 抛出"容器已冻结"异常 |
| P-03 | 池实例被场景卸载带走 | `Rent()` 不返回 fake-null；`Created == Destroyed` 仍对账 |
| P-04 | `DisposeAsync(purgeAssets: true)` | 本 Scope 独占资源被卸载，`ActiveLeases` 归零，内存回落 |
| A-05 | 同址 `AcquireAsync` + `AcquireCacheAsync` 混用 | 底层只加载一次；两种租约各自独立计数 |
| A-06 | `ct` 已取消时 `AcquireAsync` | 立即失败，不产生底层请求，`PendingRequests` 不变 |
| A-07 | 全部等待者取消 | 底层句柄被回收，计数归零，允许卸载 |

### 9.5 修改顺序

1. **改 `Dispose` 为 `DisposeAsync`**（签名问题，改动最便宜）
2. **补热更链路与 `InitializeAsync` 到 `IAssetService`**（不补，硬规则第一天就破）
3. **把"缓存保留 / 强制卸载"拆成两条策略**（不拆，切场景内存不回落的问题会原样重现）
4. 其余（H-02、G-01、G-02）在写实现前补进 §4 / §5，因为它们决定 `ModuleHub` 与 `PoolService` 是否需要改动——而这两个在 §4.2 里写的是"不变"。

### 9.6 修订后的契约（供对照替换）

**§2 资源层**

```csharp
public interface IAssetService
{
    // 生命周期：由 App 在启动链路中显式调用，且必须先于依赖资源的模块
    UniTask InitializeAsync(CancellationToken ct);

    // 加载：key 用 object，以同时接受 string address 与 AssetReference
    // 同址并发请求共享底层加载，但各自拿到独立租约（语义见 §2）
    UniTask<AssetLease<T>> AcquireAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object;

    // 缓存保留：不参与业务引用计数，用于池/常驻资源保活
    UniTask<AssetLease<T>> AcquireCacheAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object;

    // 热更（原契约缺失，业务不得绕过本层直接调 Addressables）
    UniTask<bool> CheckForUpdatesAsync(CancellationToken ct);              // true = 有新 catalog
    UniTask<int>  UpdateCatalogsAsync(CancellationToken ct);               // 返回更新的 catalog 数
    UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct);
    UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct);

    // 诊断
    int PendingRequests { get; }
    int ActiveLeases { get; }
    IReadOnlyList<LeaseInfo> DumpLeases(object key = null);                 // 按 key 过滤，null = 全部
}

public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
{
    public T Asset { get; }        // IsReleased 时 getter 抛 InvalidOperationException（开发期）
    public string Address { get; }
    public bool IsReleased { get; }
    public void Dispose();         // 幂等；重复释放不改变其它租约计数
}
```

> **为什么 `AssetLease` 保持 `class` 而不是改成 `struct`**：`struct` 被复制后任意一份 `Dispose` 都会归还，直接造成重复归还 → 计数错乱；`class` 的"多份引用同一对象 + 幂等 Dispose"才是正确的所有权语义。所以"每次 Rent 一次分配"不能靠改 struct 解决，而应靠 **`Rent` 不产生新租约**（池级保活租约只在预热时拿一次）。

**§4 Scope**

```csharp
public enum ScopeState { Created, Starting, Running, Stopping, Stopped, Faulted }

public sealed class Scope : IDisposable
{
    public string Name { get; }
    public Scope Parent { get; }
    public ScopeState State { get; }
    public ModuleHub Modules { get; }    // 仅 Created / Starting 阶段可 Register，进入 Running 后冻结
    public PoolService Pools { get; }

    public Scope CreateChild(string name);   // 仅 Running 状态允许；否则抛异常

    public UniTask StartupAsync(CancellationToken ct, IStartupProgress progress = null);
    public UniTask DisposeAsync(bool purgeAssets = false);   // 逆序：子 → 模块 → 池 → 租约

    void IDisposable.Dispose();   // 同步兜底：已 Stopped 则幂等；否则记 Error 并强制释放
}
```

**关闭顺序（含 purge）**

```
1. 子 Scope.DisposeAsync()
2. 模块 ShutdownAsync()               ← 业务停止使用资源
3. 池 DisposeAsync(force: true)        ← 销毁实例，归还池级保活租约
4. 其余租约 Dispose()                  ← 计数归零
5. purgeAssets ? 资源层强制卸载本 Scope 独占资源
               : 交给缓存策略（LRU + 上限）
```

**跨 Scope 解析规则（H-02 的答案，建议取 ②）**

```
ModuleHub 增加 ParentHub。解析顺序：先查自己 → 再查父级 → 直到根 Scope 停止。
约束：
  · 只在 Running 状态允许解析（Starting 阶段解析会拿到未初始化实例）
  · 解析结果缓存到模块字段，不每帧查
  · 禁止反向：父 Scope 不得解析子 Scope 的模块

---

## 10 实测：AssetBundle 卸载语义（2026-11-15）

**探针**：`Assets/Aot/Editor/BundleUnloadProbe.cs`（`-executeMethod NowFrameWork.Probe.BundleUnloadProbe.Run`）
**报告**：`EXE/_evidence/ab-unload-probe.txt`

| 场景 | 实测结果 |
|---|---|
| `Unload(false)` 后立即 | 资源**仍可用** |
| `Unload(false)` + `UnloadUnusedAssets` | 仍可用（探针持有托管引用） |
| `Unload(true)` 后立即 | 资源**立即不可用**：`InstanceIDToObject` 返回 null，托管引用变 fake-null |

**源码对照**：Addressables 1.22.4-t1 的 `AssetBundleProvider.Unload()` 调用 `m_AssetBundle.Unload(true)`
（`Runtime/ResourceManager/ResourceProviders/AssetBundleProvider.cs:885`，注释原文："Releases the asset bundle via AssetBundle.Unload(true)"）。

### 10.1 由此得到的硬规则 R-1

> **禁止在销毁实例的同一帧释放租约。**

原因链：

```
Object.Destroy(go)        → 延迟到帧末才真正销毁
lease.Dispose()           → 计数归零 → Addressables 立即 Unload(true)
                          → 本帧剩余时间里，对象还在，但它的资源已经没了
```

正确做法（`Scope.DisposeAsync` / 池整池释放）：

```csharp
DestroyAllInstances();          // 同步调用 Object.Destroy
await UniTask.NextFrame();      // 等 Destroy 在本帧末生效
ReleaseKeepAliveLease();        // 再释放池级保活租约
```

**闲置回收不受影响**：自动回收只销毁实例，不动池级保活租约，资源不会被卸载。
```
