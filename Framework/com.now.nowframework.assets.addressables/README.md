# NowFrameWork.Assets.Addressables

**全工程唯一直接引用 Addressables 的包。** 实现 `IAssetProvider` / `IAssetUpdateProvider`。

## 用法

```csharp
var (assets, assetsModule) = AddressablesAssets.Create(timeout: TimeSpan.FromSeconds(30));
hub.Register(assetsModule);          // 启动时初始化；关闭时检查租约泄漏

AssetLease<GameObject> lease = await assets.AcquireAsync<GameObject>("Assets/Prefabs/Enemy.prefab", ct);
// ... 使用 lease.Asset ...
lease.Dispose();
```

## 实现要点

| 点 | 说明 |
|---|---|
| canonical 键 | 用 `PrimaryKey`，地址与 GUID 指向同一资源时会被判为同一份 |
| 取消 | 只放弃等待，不取消底层共享加载 |
| 超时 | 默认 30s；远端不可达时抛 `TimeoutException`，不会无限挂起 |
| 释放 | `Addressables.Release(handle)`，句柄存在 `ProviderHandle.Tag` 里 |

## 实测记录

- 远端 bundle 加载：已验证（Player）
- 资源卸载：**异步**，释放后 1~2 帧才失效 → 见 `docs/13 §9.3~9.5`
- 失败路径（服务器不可达）：**尚未验证**
