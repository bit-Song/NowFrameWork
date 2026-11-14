# 13 Scope 与资源层设计

> 对应缺口 **G-05**（[12](12-框架缺口清单.md)）。本文定契约与时序，不含实现；实现排在资源里程碑。
> 已定前提：[05 §2 租约语义](05-技术方案-Addressables与HybridCLR.md)、[ADR-004](07-架构决策记录ADR.md)、[ADR-025](07-架构决策记录ADR.md)、[11](11-对象池与资源租约.md)。
> 评审记录与处理对照见 [§10](#10-评审意见处理对照2026-11-15)。

## 1 要解决什么

| 问题 | 现状 | 本文的答案 |
|---|---|---|
| 场景级服务无处安放 | 所有模块都是全局单例 | `Scope` 树：每个 Scope 拥有自己的模块与池 |
| 切场景无法逆序释放 | 只能靠手写 | 关闭顺序固定：子 → 模块 → 池 → 租约 |
| 资源何时能卸载说不清 | 只有计数，没有归属 | 租约归属到 Scope，关闭才允许卸载 |
| 池里残留已卸载资源的实例 | 无约束 | 池级保活租约挂在 Scope 上，先清池再放租约 |
| 场景 Scope 拿不到根服务 | 未定义 | **构造注入**：依赖写在构造函数上，组合根负责装配（§4.3） |

## 2 资源层契约

```csharp
public interface IAssetService
{
    // 生命周期：由 App 在启动链路显式调用，必须先于依赖资源的模块
    UniTask InitializeAsync(CancellationToken ct);

    // key 用 object：同时接受 string address 与 AssetReference.RuntimeKey
    // 同址并发共享底层加载，但各自得到独立租约（语义见 §2.1）
    UniTask<AssetLease<T>> AcquireAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object;

    // 缓存保留：不参与业务引用计数，用于池/常驻资源保活
    UniTask<AssetLease<T>> AcquireCacheAsync<T>(object key, CancellationToken ct) where T : UnityEngine.Object;

    // 热更：业务不得绕过本层直接调 Addressables
    UniTask<bool> CheckForUpdatesAsync(CancellationToken ct);
    UniTask<int> UpdateCatalogsAsync(CancellationToken ct);
    UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct);
    UniTask DownloadAsync(object key, IProgress<float> progress, CancellationToken ct);

    // 诊断
    int PendingRequests { get; }
    int ActiveLeases { get; }
    IReadOnlyList<LeaseInfo> DumpLeases(object key = null);
}

public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
{
    public T Asset { get; }     // IsReleased 时：Editor/Development 抛异常，Release 返回 null
    public string Address { get; }
    public bool IsReleased { get; }
    public long Version { get; }   // 诊断用：同一个 key 的多次申请可区分
    public void Dispose();         // 幂等；不影响其它租约的计数
}
```

### 2.1 三条租约语义（来自 [05 §2](05-技术方案-Addressables与HybridCLR.md)）

1. **同址双请求 = 两个租约**：释放 A 不使 B 失效，重复释放 A 不改变 B 的计数；
2. **取消只结束该等待者**：其余等待者继续；底层最终完成也不交付给已取消者；
3. **失败也要回收底层句柄**：不允许"加载失败但句柄泄漏"。

### 2.2 为什么 `AssetLease` 是 class 不是 struct

struct 被复制后任意一份 `Dispose` 都会归还，直接造成重复归还 → 计数错乱。 class 的"多引用 + 幂等 Dispose"才是正确的所有权语义。

因此"每次租用一次分配"不能靠改 struct 解决，而靠 **`Rent` 不产生新租约**：池级保活租约只在预热时拿一次（见 [11 §8](11-对象池与资源租约.md)）。

## 3 Provider 边界

```csharp
public interface IAssetProvider
{
    UniTask<ProviderHandle> LoadAsync(object key, CancellationToken ct);
    void Release(ProviderHandle handle);        // 幂等
    UniTask<long> GetDownloadSizeAsync(object key, CancellationToken ct);
}

public sealed class ProviderHandle : IDisposable
{
    public UnityEngine.Object Asset { get; }
    public bool IsValid { get; }
    public void Dispose();                      // 等价于 Provider.Release
}
```

- `AddressablesProvider` 是首个实现；其它模块**只依赖 `IAssetService`**（[06 §2](06-工程规范-测试与验收.md) 硬规则）；
- **`Release` 返回后资源可能已不可用**：Addressables 1.22.4-t1 在 `AssetBundleProvider.Unload()` 中调用 `AssetBundle.Unload(true)`（实测见 §9），资源立即失效。
  "Release 到 bundle 真正卸载"之间还有内存未回落的窗口，诊断面板不应把 `ActiveLeases == 0` 当作"内存已回收"。

## 4 Scope

```csharp
public enum ScopeState { Created, Starting, Running, Stopping, Stopped, Faulted }

public sealed class Scope : IDisposable
{
    public string Name { get; }
    public Scope Parent { get; }
    public ScopeState State { get; }
    public ModuleHub Modules { get; }   // 仅 Created/Starting 可 Register，进入 Running 后冻结
    public PoolService Pools { get; }
    public AssetLease<SceneAsset> SceneLease { get; }   // 场景以 Scope 为单位持有

    public Scope CreateChild(string name);              // 仅 Running 允许，否则抛异常
    public UniTask StartupAsync(CancellationToken ct, IStartupProgress progress = null);
    public UniTask DisposeAsync(bool purgeAssets = false);

    void IDisposable.Dispose();   // 同步兜底：已 Stopped 幂等；否则记 Error 并强制释放
}
```

状态机约束：

| 调用 | 允许的状态 | 违反时 |
|---|---|---|
| `CreateChild` | Running | 抛 `InvalidOperationException` |
| `StartupAsync` | Created | 抛异常 |
| `DisposeAsync` | 任意（幂等） | 已 Stopped 直接返回 |
| `Modules.Register` | Created / Starting | 由 `ModuleHub` 抛"容器已冻结" |

### 4.1 关闭顺序

```
1. 子 Scope.DisposeAsync()
2. 模块 ShutdownAsync()            ← 业务停止使用资源
3. 池 Dispose(force: true)          ← 销毁实例（同步）
4. await NextFrame()                ← 等 Object.Destroy 生效（硬规则 R-1，见 §9）
5. 释放池级保活租约 + 其余租约
6. 卸载场景（SceneLease.Dispose）
7. purgeAssets ? 资源层强制卸载本 Scope 独占资源
                : 交给缓存策略（LRU + 上限）
```

**为什么池必须先于租约**：池持有保活租约，先放租约再清池，销毁实例期间资源可能已被卸载。
**为什么第 4 步不能省**：`Object.Destroy` 延迟到帧末，而 `Unload(true)` 立即失效（§9 实测）。

### 4.2 与现有组件的关系

| 组件 | 变化 |
|---|---|
| `ModuleHub` | **不改**；每个 Scope 各持一个实例 |
| `PoolService` / `ObjectPool` | **不改**；由 Scope 持有，关闭时同步 `Dispose(force: true)` |
| `AssetPool`（资源包） | 新增：持有保活租约 + 一个 `ObjectPool<T>`，负责第 4 步的跨帧释放 |
| `App` | 增加 `RootScope`，`Hub` 改为 `RootScope.Modules` 的转发（保持兼容） |

### 4.3 跨 Scope 解析：构造注入（H-02 的答案）

**规则：模块的依赖全部写在构造函数上；`Get<T>()` 只允许出现在装配代码里。**

```csharp
public sealed class LevelDirector : IAppModule
{
    readonly IAudioService _audio;   // 根 Scope 的全局服务
    readonly PoolService _pools;     // 本 Scope 的池

    public LevelDirector(IAudioService audio, PoolService pools)   // 依赖清单 = 签名
    {
        _audio = audio;
        _pools = pools;
    }

    public string Id => "LevelDirector";
    public IReadOnlyList<string> DependsOn => System.Array.Empty<string>();
    public UniTask InitializeAsync(CancellationToken ct) { _audio.PlayBgm("level_01"); return UniTask.CompletedTask; }
    public UniTask StartAsync(CancellationToken ct) => UniTask.CompletedTask;
    public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
}
```

装配代码（全工程唯一查容器的地方）：

```csharp
public static class LevelScopeFactory
{
    public static async UniTask<Scope> CreateAsync(Scope root, string levelId, CancellationToken ct)
    {
        var scope = root.CreateChild("Level:" + levelId);

        var audio  = root.Modules.Get<IAudioService>();     // ← 组合根
        var assets = root.Modules.Get<IAssetService>();

        scope.Modules.Register(new LevelDirector(audio, scope.Pools));
        scope.Modules.Register(new EnemySpawner(assets, scope.Pools));

        await scope.StartupAsync(ct);
        return scope;
    }
}
```

依赖过多时收敛成上下文对象（仍是构造注入）：

```csharp
public readonly struct RootServices
{
    public readonly IAudioService Audio;
    public readonly IAssetService Assets;
    public readonly IEventBus Events;
}
```

| 对比项 | ParentHub 查找 | **构造注入（选定）** |
|---|---|---|
| 依赖可见性 | 藏在方法体 | 就是签名 |
| 忘注册 | 运行时才报错 | 装配代码编译不过 |
| 单测 | 要建真 Hub | `new Module(fakeA, fakeB)` |
| `ModuleHub` | 要加 ParentHub 与解析链 | 不改 |

> 强制手段：先靠 Review（[06 §2](06-工程规范-测试与验收.md) 规则表），T05 的 Roslyn 分析器可把"模块类里调用 `Get<T>()`"变成构建期错误。

## 5 关键时序

### 5.1 进入场景

```
scope = root.CreateChild("Level01")
await scope.StartupAsync(ct, progress)
  → 模块 Initialize（AssetPool 预热：AcquireCacheAsync + 预创建实例）
  → 模块 Start
```

### 5.2 切换场景（含失败回滚）

```
newScope = root.CreateChild("Level02")
try
{
    await newScope.StartupAsync(ct)      // 新场景加载完成
    await oldScope.DisposeAsync()        // 成功才释放旧的
}
catch
{
    await newScope.DisposeAsync(force)   // 失败：逆序清理新的
    // 旧 Scope 保持 Running，玩家不卡在中间态
}
```

先加载后释放：避免黑屏期，代价是峰值内存高。低内存设备可改为先释放后加载——策略由项目决定，框架不强制。

**注意峰值**：两个 Scope 同时存在期间，共享资源靠同址共享只加载一份，但**池是每个 Scope 一份**，各自预热会各建一批实例。

### 5.3 运行时借用

```
pool.Get()            → 有闲置：同步返回；无闲置且未达上限：同步创建；达上限：抛 PoolExhaustedException
pool.TryGet(out item) → 只取闲置，不创建
pool.RentAsync(ct)    → 有闲置：同步返回；否则名额预留 + 异步创建
```

两条必须写死：

1. **`Rent` 不产生新租约**——资源由池级保活租约保证，每次借用都申请租约会让计数抖动；
2. **`Get()` 在达上限时抛异常**，不返回 null，避免空引用在业务深处爆掉。

### 5.4 场景加载与租约归属

| 场景来源 | 加载方式 | 租约归属 |
|---|---|---|
| 本地场景（Build Settings 内） | `SceneManager.LoadSceneAsync` | 无资源租约（随 Build 打包） |
| 远端/分包场景 | `IAssetService.AcquireSceneAsync`（内部走 Addressables SceneProvider） | **`Scope.SceneLease`** |

- 场景实例化的物体随场景卸载销毁，**不进入对象池**；
- `SceneLease.Dispose()` 在 Scope 关闭流程的第 6 步，必须晚于模块与池；
- 同址共享：两个 Scope 加载同一场景（少见）时各自持有租约。

## 6 失败矩阵

| 情况 | 必须的行为 |
|---|---|
| 资源加载失败 | 租约不产生；等待者收到异常；底层句柄回收 |
| 等待中取消 | 只移除该等待者；其余继续；不产生租约 |
| 全部等待者取消 | 按 Provider 能力取消底层操作，或登记完成后立即释放 |
| Scope 启动失败 | 逆序清理：池 → 已启动模块 → 子 Scope |
| Scope 切换时新 Scope 失败 | 旧 Scope 保持可用；新 Scope 逆序清理 |
| Scope 关闭中仍有对象借用 | 池 `Dispose(force: true)` 强杀，记 Error（经 ErrorBudget 聚合） |
| 资源已卸载仍被使用 | **不该发生**（保活租约挡住）；若发生由池的 `isValid` 兜底并记 Error |
| 同帧销毁实例并释放租约 | **禁止**（规则 R-1）：必须跨一帧 |
| 预热部分失败 | 已创建实例全部销毁、已申请租约全部释放，再抛出 |

## 7 验收用例

| 编号 | 场景 | 断言 |
|---|---|---|
| A-01 | 同址并发两次 Acquire | 两个不同租约；释放其一另一个仍可用 |
| A-02 | 重复释放同一租约 | 幂等，不改变其它租约计数 |
| A-03 | 等待中取消 | 只有该等待者失败，其它成功 |
| A-04 | 加载失败 | 不产生租约，计数归零 |
| A-05 | `AcquireAsync` 与 `AcquireCacheAsync` 混用同址 | 底层只加载一次；两种租约各自独立计数 |
| A-06 | `ct` 已取消时 Acquire | 立即失败，不产生底层请求，`PendingRequests` 不变 |
| A-07 | 全部等待者取消 | 底层句柄回收，计数归零，允许卸载 |
| S-01 | Scope 关闭 | 顺序为 子 → 模块 → 池 → 租约，且池销毁后租约才归零 |
| S-02 | Scope 启动失败 | 逆序清理，无残留池与租约 |
| S-03 | 切场景时新 Scope 启动失败 | 旧 Scope 完好可用；新 Scope 无残留 |
| S-04 | `Dispose` 中 `CreateChild`；`StartupAsync` 中 `Dispose` | 明确抛错，不产生半关闭状态 |
| S-05 | `StartupAsync` 完成后 `Register` | 抛"容器已冻结"异常 |
| P-01 | 池持有保活租约时请求卸载 | 资源不会被卸载 |
| P-02 | 池 Dispose 后 | 租约计数归零，资源可卸载 |
| P-03 | 池实例被场景卸载带走 | `Rent` 不返回 fake-null；`Created == Destroyed` 仍对账 |
| P-04 | `DisposeAsync(purgeAssets: true)` | 本 Scope 独占资源被卸载，`ActiveLeases` 归零 |
| P-05 | 销毁实例与释放租约的时序 | 释放前至少跨一帧；期间对象仍可安全访问 |

## 8 待拍板（剩余）

| # | 问题 | 建议 |
||---|---|---|
| 1 | 池键是否需要变体维度 | 需要。同一 prefab 可能有不同父节点/初始状态，键做成 `(地址, 变体)` |
| 2 | 缓存淘汰策略 | LRU + 容量上限，由资源层配置；`purgeAssets` 只用于强制场景 |
| 3 | UI/音频是否纳入 Scope | 分层：UI 根与音频常驻根 Scope，场景级 HUD 随场景 Scope |
| 4 | HUD 淡出时被强杀 | **业务策略**，框架不内置"等动画"；提供 `HudController` 自行延迟到动画结束再让 Scope 关闭 |

## 9 实测：AssetBundle 卸载语义（2026-11-15）

**探针**：`Assets/Aot/Editor/BundleUnloadProbe.cs`（`-executeMethod NowFrameWork.Probe.BundleUnloadProbe.Run`）
**报告**：`EXE/_evidence/ab-unload-probe.txt`

| 场景 | 实测结果 |
|---|---|
| `Unload(false)` 后立即 | 资源**仍可用** |
| `Unload(false)` + `UnloadUnusedAssets` | 仍可用（探针持有托管引用） |
| `Unload(true)` 后立即 | 资源**立即不可用**：`InstanceIDToObject` 为 null，托管引用变 fake-null |

**源码对照**：Addressables 1.22.4-t1 `AssetBundleProvider.Unload()` → `m_AssetBundle.Unload(true)`（`Runtime/ResourceManager/ResourceProviders/AssetBundleProvider.cs:885`）。

### 9.1 硬规则 R-1

> **禁止在销毁实例的同一帧释放租约。**

```
Object.Destroy(go)  → 延迟到帧末
lease.Dispose()     → 计数归零 → Addressables 立即 Unload(true)
                    → 本帧剩余时间：对象还在，资源已死
```

闲置回收不受影响：自动回收只销毁实例，不动池级保活租约。

## 10 评审意见处理对照（2026-11-15）

| 编号 | 评审意见 | 处理 |
|---|---|---|
| H-01 | `Dispose` 同步无法等 `ShutdownAsync` | ✅ 已改 `DisposeAsync(bool purgeAssets)` + `IDisposable` 同步兜底 |
| H-02 | 跨 Scope 拿根服务未定义 | ✅ 定为**构造注入**，见 §4.3 |
| H-03 | 缓存保留与"关闭才允许卸载"冲突 | ✅ 拆成两条：缓存策略 + `purgeAssets` 强制卸载（§4.1 第 7 步） |
| M-01 | 缺热更链路 | ✅ 已补 `CheckForUpdates/UpdateCatalogs/GetDownloadSize/Download` |
| M-02 | 缺 `InitializeAsync` | ✅ 已补 |
| M-03 | `AcquireCacheAsync` 丢类型 | ✅ 改为泛型 `AcquireCacheAsync<T>` |
| M-04 | 只收 `string address` | ✅ 改为 `object key`（兼容 `AssetReference.RuntimeKey`） |
| M-05 | `ProviderHandle` 未定义 | ✅ 已定义（§3），含幂等 `Release` 与"立即失效"语义 |
| G-01 | 切场景失败无回滚 | ✅ §5.2 补失败回滚 |
| G-02 | `Rent()` 未命中行为未定义 | ✅ §5.3 写死：不创建 / 达上限抛异常 |
| G-03 | Scope 缺状态机 | ✅ §4 补 `ScopeState` 与状态-操作表 |
| G-04 | 场景加载归属不明 | ✅ §5.4 补表 |
| G-05 | 释放后 `Asset` 仍可访问 | ✅ 改为 Editor/Development 抛异常 |
| G-06 | `isValid` 定义与检查点 | ✅ 池取出前检查 + fake-null 判定（[11 §7](11-对象池与资源租约.md)） |
| G-07 | 池键单变体 | ✅ §8 待拍板第 1 条，倾向 `(地址, 变体)` |
| G-08 | 预热部分失败回滚 | ✅ §6 失败矩阵补一行 |
| — | `PoolService.DisposeAsync` 建议 | ❌ 不采纳：池销毁是同步的，跨帧等待属于租约持有者（`AssetPool`） |
| — | HUD 淡出被强杀 | ❌ 框架不内置：业务策略（§8 第 4 条） |
| **R-1** | （评审未提，实测发现）同帧销毁+释放租约 | ✅ §4.1 第 4 步 + §9 实测 + §7 用例 P-05 |