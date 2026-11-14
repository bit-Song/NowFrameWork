# com.now.nowframework.unity

NowFrameWork 的 Unity 适配层，**唯一允许引用 UnityEngine 的包**。

## 引用方式

```json
"com.now.nowframework.unity": "file:../../Framework/com.now.nowframework.unity"
```

> 相对路径从工程的 `Packages` 目录算。

## 内容

| 类型 | 作用 |
|---|---|
| `UnityClock : IClock` | 读 `Time.timeAsDouble` / `unscaledTimeAsDouble` / `deltaTime` |
| `UnityConsoleSink : ILogSink` | 映射到 `Debug.Log/LogWarning/LogError`（全工程唯一调用 Debug 的地方） |
| `App` | 门面：Log / Events / Timers / Hub，以及帧驱动顺序 |
| `GameBootstrap : MonoBehaviour` | 场景入口：装配 App、注册模块、转发 Unity 回调、关闭 |

## 帧驱动顺序

```
Update:      Timers.Update() -> Hub.Tick() -> Events.FlushDeferred()
LateUpdate:  Hub.LateTick()
FixedUpdate: Hub.FixedTick()
```

顺序固定写在 `App.Tick` 里，不允许业务自行调整——延迟事件必须在所有模块 Tick 之后派发。

## 快速开始

1. 菜单 **NowFrameWork / 创建 Boot 场景** 生成 `Assets/Scenes/Boot.unity`（已挂 GameBootstrap）
2. 把它加入 Build Settings 的 Scenes In Build
3. 继承 `GameBootstrap` 并重写 `Configure(App app)` 注册模块：

```csharp
protected override void Configure(App app)
{
    app.Hub.Register(new AudioModule(app));
}
```

## 约束

- 本包不定义任何业务接口，只做适配；
- 启动失败后 `StartupFailed` 置位，不再驱动帧回调（避免半启动状态继续跑）；
- `destroyCancellationToken` 传入 Startup，场景销毁时自动取消。