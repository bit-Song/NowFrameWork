# 23 · Unity 手动流程测试与 API

> 2026-11-17。面向当前工程的调用示例，不依赖 UI 包。启动宿主仍使用 BootFlow；它自带的 IMGUI 状态文字/停止按钮只是启动调试入口。

## 1. 直接运行现成场景

1. 打开 `Assets/Scenes/FrameworkApiSample.scene`。如需重新生成，菜单为 **NowFrameWork → Tests → API → 创建完整流程测试场景（不含UI）**；已有场景不会被覆盖。
2. Addressables Groups 的 **Play Mode Script** 选择 **Use Asset Database (fastest)**，用于先测试本地 API 流程，无需远端服务器。场景里的 `BootFlow` 保持 `Check Resource Updates=false`、`Load Code=false`，配置/场景地址留空；`Project Entry` 已绑定同对象的 `FrameworkApiSample`。
3. 点击 Play，等待 Console 中 `[API] 入口已就绪` / `[Boot] 启动完成`。
4. Hierarchy 选中 `FrameworkApiSample`（可能位于 DontDestroyOnLoad 场景），在同名组件右上菜单依次选择：
   - **1.运行完整 API 流程**：事件 → App.Tick 计时器 → 配置 → Prefab/资源池 → 销毁并归还租约 → 附加场景 → 卸载。
   - **2.输出资源诊断**：输出 Pending / ActiveLeases / Configs 及租约明细。
   - **3.保存测试语言 zh-CN**：显式写入 BootSample/preferences；停止并重新启动后可查看读取结果。
5. 点击 Game 窗口左上角 Boot 的 **取消 / 停止**，等待“停止与清理完成”。也可在自己的代码里 `await flow.Stop()`。先停止，再结束 Play，以便 PlayerLoop 能继续完成销毁等待。

`PCProbe/Prefab` 是空的探针 GameObject，没有 Mesh；观察 Hierarchy 中短暂出现的 `ApiSample_PooledInstance` 和 Console，Game 窗口不会出现可见模型。组件的 Hold Seconds 可调长，方便观察实例及附加场景。

完整测试结束时配置缓存通常持有 1 个租约，这是 ConfigService 的正常生命周期；重复运行不会一直增加。Boot 停止时会释放配置并检查资源租约归零。资源更新和 HybridCLR DLL 加载由下面的独立流程验证，不在默认本地测试中自动执行。

## 2. 已添加的入口接口

源码：`Assets/Aot/Boot/FrameworkApiSample.cs`，程序集 `NowFrameWork.Boot`。

```csharp
// Inspector 显式拖引用，不依赖 FindObjectOfType 或静态单例。
[SerializeField] NowFrameWork.Boot.BootFlow flow;
[SerializeField] NowFrameWork.Boot.FrameworkApiSample sample;

await flow.Run();                      // 装配并启动；失败由 BootFlow 记录到 Console
await sample.RunAllAsync(ct);           // 启动成功后，运行完整本地测试
sample.PrintDiagnostics();              // 查看在途加载和租约
sample.SaveLanguage("zh-CN");           // 显式保存测试设置
await flow.Stop();                     // 退出入口、清理实例/场景/配置、关闭 App
```

`RunAllAsync` 串行执行，多次同时调用会报忙；首次失败后若仍持有池或场景，先调用 Stop 重试清理。它会等待由 BootFlow.Tick 驱动的计时器，不能放到 `IBootEntry.EnterAsync` 内等待，否则启动尚未进入 Running 就没有帧驱动。

如果调用方位于自己的 asmdef，显式引用 `NowFrameWork.Boot`、`NowFrameWork.Boot.Addressables`、`NowFrameWork.Core`、`NowFrameWork.Unity`、`NowFrameWork.Assets`、`UniTask`（仅引用实际用到的程序集）。这些包关闭了 Auto Referenced，普通 Assembly-CSharp 不会自动获得所有引用。

## 3. 从项目入口取得服务

实际项目通过 `IBootEntry` 接收上下文，不需要 FrameworkApiSample。

```csharp
public UniTask EnterAsync(BootContext context, CancellationToken ct)
{
    _context = context;                // 借用，BootSession 持有基础服务
    // _context.App：模块、日志、事件、计时器、平台、生命周期
    // _context.Assets：资源和租约；_context.Configs：常驻配置
    // _context.Scenes：场景服务；_context.Settings：已加载的设置
    return UniTask.CompletedTask;
}

public async UniTask ExitAsync()
{
    // 先等待自己创建的实例/池/场景/任务结束，并归还自己的租约。
    // 成功后才清引用；失败保留引用供下一次 ExitAsync 重试。
}
```

Boot 的基础 Hub 已启动，不能在 Enter 中继续往 `context.App.Hub` 注册模块。项目模块需要独立 Scope，在 Scope.StartupAsync 前登记。Scope 不自动追踪任意局部变量里的资源租约，仍须明确登记/释放各自所有物。

## 4. 单项资源、配置与场景调用

以下的 `context` 来自 EnterAsync，或测试期间的 `sample.Context`；所有调用在 Unity 主线程，异步方法接收 `CancellationToken ct`。

```csharp
// 普通资源：申请独立租约；using 结束后不要继续使用该资源。
using (AssetLease<TextAsset> lease =
       await context.Assets.AcquireAsync<TextAsset>("PCProbe/Config", ct))
{
    Debug.Log(lease.Asset.text);
}

// 配置缓存：租约由 ConfigService 持有，重复同址加载复用缓存。
TextAsset config = await context.Configs.LoadAsync<TextAsset>("PCProbe/Config", ct);
Debug.Log(config.text);
// 不在这里 Dispose context.Configs；Boot 退出时统一清理。

// 场景：独立租约，异步卸载；不要用 AssetLease 加载 Scene。
SceneLease scene = await context.Scenes.LoadAdditiveAsync("PCProbe/Scene", ct);
try { /* 在附加场景中执行测试 */ }
finally { await scene.UnloadAsync(); }

// 设置：Boot 已 Load；修改后需要显式 Save，不会自动保存。
context.Settings.Value.Language = "zh-CN";
context.Settings.Save();
Debug.Log(context.Settings.FilePath);

// 诊断：配置缓存也会计入 ActiveLeases。
Debug.Log($"Pending={context.Assets.PendingRequests}, Leases={context.Assets.ActiveLeases}");
```

Prefab 实例不要使用“using 加载 → Instantiate → 立刻退出 using”的写法。对象仍存活时必须保留租约；完整例子见 `FrameworkApiSample.TestPoolAsync`：临时申请 Prefab → 建立池保活租约 → 释放临时租约 → Rent/Release → await 池 DisposeAsync 等对象真正销毁后归还池租约。

## 5. 资源更新接口与时机

资源更新应在加载配置、实例、场景、DLL 之前进行。默认样例的 Boot Config Address / Scene Address 为空，所以可以在第一次运行完整测试前，从自己的代码调用：

```csharp
var update = new HotUpdateService(context.Assets, context.App.Log);
// 此处仅演示一个资源键；正式启动应传覆盖本次所有启动内容的 Label。
HotUpdatePlan plan = await update.PrepareAsync("PCProbe/Prefab", ct);
Debug.Log($"CatalogChanged={plan.HasUpdate}, DownloadBytes={plan.DownloadBytes}");
await update.DownloadAsync("PCProbe/Prefab",
    Cysharp.Threading.Tasks.Progress.Create<float>(p => Debug.Log($"Download: {p:P0}")), ct);
// Prepare 和 Download 使用同一个键；完成后再加载业务内容。
```

也可以在 BootFlow Inspector 开启 **Check Resource Updates** 并填写 **Update Key**，由 BootSession 在配置加载前执行 Prepare → Download。不要在完整样例已经加载配置缓存后再更新 Catalog；先停止并重新启动。

**Use Asset Database 只用于本地调用与所有权测试，不能证明远端资源更新生效。** 验证远端更新时改用 **Use Existing Build**，先构建 Addressables Catalog/Bundle 并启动对应服务器。工程已有 PCRegression 本地服务器、V1/V2 资源和 Player 脚本，步骤见 [17](17-PC回归入口与实测.md)。远端探针键为 `PCProbe/RemoteData`，服务器地址是该回归 Profile 配置的 `http://127.0.0.1:18182/[BuildTarget]`。

## 6. 可选代码热更与验证边界

编辑器默认不加载真实 HybridCLR DLL。需要测试代码热更时，在 Player 上使用现有 `HybridClrBootLoader`/BootFlow Code Loader 接入，或运行文档 17 的 V1/V2 回归；它和资源 Prepare/Download 是两个独立步骤，DLL 进入进程后不能同进程回滚替换。

本次交付增加可调用示例、场景及菜单，已通过编辑器编译和场景创建；随后全量 EditMode **301/301 通过，0 失败、0 跳过**，本机报告 `Build/ApiSample-20261117/editmode.xml`。实际 Play 操作由用户按以上步骤执行；没有把自动 EditMode 回归当作新场景端到端通过的证明。默认流程不引用 UI 包、不创建 UGUI 页面。
