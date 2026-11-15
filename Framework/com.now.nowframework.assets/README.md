# NowFrameWork.Assets

资源层：加载键、租约、引用计数、作用域、对象池。**不直接依赖 Addressables**。

## 分层

```
业务 → IAssetService（本包）
         ├─ AssetKey        加载键（地址 / 子对象 / 变体）
         ├─ AssetLease<T>   租约，Dispose 归还
         ├─ AssetPool<T>    池级保活租约 + 对象池
         └─ Scope           生命周期：子 Scope → 模块 → 池 → 等卸载
```

真正加载的是 `IAssetProvider`，由 `com.now.nowframework.assets.addressables` 提供实现。

## 关键规则

| 规则 | 说明 |
|---|---|
| 同址并发只加载一次 | 每个等待者各得独立租约，计数由 `AssetService` 负责 |
| 单个等待者取消不影响共享加载 | 全部取消时，加载完成后立即归还 |
| **释放租约前，最后一个实例必须已销毁** | 用条件等待（实例为 null），不是数帧；帧数只做兜底 |
| 加载失败不产租约 | 条目清理，可重试 |

## 诊断

- `IAssetService.ActiveLeases` / `PendingRequests`：当前租约与在途加载数
- `AssetService.Validate(out error)`（internal）：计数不变量自检，测试用
- `AssetsModule.ShutdownAsync`：关闭时若仍有租约，记 Error
