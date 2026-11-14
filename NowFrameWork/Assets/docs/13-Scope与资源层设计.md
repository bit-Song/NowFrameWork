# 13 Scope 与资源层设计

> 对应缺口 **G-05**（[12](12-框架缺口清单.md)）。本文只定契约与时序，不含实现；实现排在资源里程碑。
> 已定前提：[05 §2 租约语义](05-技术方案-Addressables与HybridCLR.md)、[ADR-004](07-架构决策记录ADR.md)（封装 Addressables）、[ADR-025](07-架构决策记录ADR.md)（独立租约与共享加载取消）、[11](11-对象池与资源租约.md)（池级保活租约）。

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

### 5.3 运行时借用

```
pool.Rent()            → 命中闲置：同步返回
pool.RentAsync(ct)     → 未命中：名额检查 → 异步创建
```

资源是否还在内存由池的保活租约保证，`RentAsync` 不触发加载。

## 6 失败矩阵

| 情况 | 必须的行为 |
|---|---|
| 资源加载失败 | 租约不产生；等待者收到异常；底层句柄回收 |
| 等待中取消 | 只移除该等待者；其余继续；不产生租约 |
| 全部等待者取消 | 按 Provider 能力取消底层操作，或登记完成后立即释放 |
| Scope 启动失败 | 逆序清理：池 → 已启动模块 → 子 Scope |
| Scope 关闭中仍有对象借用 | 池 `Dispose(force: true)` 强杀，记 Error（通过 ErrorBudget 聚合） |
| 资源已卸载仍被使用 | **不该发生**（保活租约挡住）；若发生则由池的 `isValid` 兜底并记 Error |

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

## 8 待拍板

| # | 问题 | 建议 |
|---|---|---|
| 1 | Scope 是否各持一个 `ModuleHub` | **是**。隔离干净；根 Scope 承载全局服务，切换场景互不影响 |
| 2 | 池键是否强制 `asset:<地址>` 前缀 | **是**。便于"按资源键整池释放"，也便于诊断 |
| 3 | 租约归零后立即卸载还是延迟 | **延迟**（缓存保留 + 可配置淘汰），由资源层策略控制；Scope 只负责归还 |
| 4 | UI/音频是否纳入 Scope | **分层**：UI 根与音频服务在根 Scope 常驻；场景级 UI（HUD）随场景 Scope |