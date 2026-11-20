# 21 · Boot 启动整合

> 2026-11-16：把已验证的设置、模块、资源及可选代码加载接成项目启动样例。BootSession/BootContext 位于 Assets/Aot/Boot，仍是接入样例；不向 Core 增加任务图、依赖注入容器或业务基类。

## 1. 启动顺序与所有权

1. Compose：工厂每次返回全新的 BootContext，包括 App、AssetService、Settings 和可选场景服务；构造过程中失败的工厂自行负责尚未交出的所有权。
2. Settings：读取类型化 BootPreferences。两份都缺失才用默认内存对象；备份恢复通过 SettingsResult 暴露；损坏/版本不兼容按存储契约处理，不静默覆盖或自动保存。
3. Modules：App.Hub 启动基础 AssetsModule，初始化 Provider。目前基础 Hub 只装配这个基础资源模块，不向已启动的 Hub 动态注册项目模块。
4. Resources：项目开关决定是否 Prepare/Download。更新在任何配置、场景或 DLL 租约创建前执行；UpdateKey 应是覆盖本次启动内容及其依赖的资源键/标签。更新失败会停止启动，不隐式离线降级或切换版本。
5. Config：配置地址为空表示此项目不需要该项；地址非空则是必需配置，加载失败阻止进入项目。旧场景如保留原样例的默认 GameConfig 地址，也按必需配置处理；不需要配置时请清空地址。示例不假定具体游戏配置类型，由项目入口校验 Configuration 的实际类型与内容。
6. Code：按项目开关调用 IBootCodeLoader。普通 AOT 跳过此步；加载返回的 Assembly 放在 Context.CodeAssembly，由项目入口选择并调用项目约定的入口。默认占位入口不执行具体游戏 DLL 的业务方法。
7. Scene：按需附加加载，保留 SceneLease。
8. Entry：调用 IBootEntry.EnterAsync，完成后 State=Running、Progress=1。设置、配置、场景和基础 App 保持存活，直到显式 Stop。

BootPreferences 仅演示 schema/language 设置，不自动修改系统语言或引擎状态。设置保存由项目明确调用 Context.Settings.Save。Inspector 中的启动策略在创建 BootSession 时快照固定，不是可由存档随意切换的热更权限。

项目入口可以创建自己的模块/Scope，负责实例、资源池、事件订阅和任务的所有权；不要在启动后的 Context.App.Hub 上继续 Register。入口 ExitAsync 须可处理 Enter 部分失败，且真正 await 项目实例/资源池收尾；如果用了 Context.App.Pools，还须逐个移除项目池（不能只清空对象却保留池注册）。样例不替项目推断“所有对象已经销毁”。

## 2. 取消、停止与重试

| 行为 | 约定 |
|---|---|
| 重复 Start | 启动中、运行中、关闭中或仍有未清理 Context 时拒绝，不创建第二个所有者 |
| 启动取消 | 在阶段边界与可取消调用中观察令牌；取消仅影响启动，不在成功后自动结束项目 |
| 启动时 Stop | 发出取消并等待启动收尾，不能在仍进行初始化时卸载其依赖 |
| 成功后 Stop | 停止帧驱动 → Exit 项目入口 → 确认基础池注册已清空 → 卸载场景 → 关闭空基础池 → 归还配置 → 等在途资源归零 → 检查租约归零 → Shutdown App |
| 入口退出/场景卸载失败/仍有池注册 | 停在 CleanupFailed，保留剩余依赖；修复原因后再次 Stop，不能抢先新建上下文 |
| 在途资源未完成 | 最长等 cleanupSeconds（默认 10 秒）；超时保留 Context，稍后可再次 Stop |
| 普通启动失败/取消 | 清理成功后可以重试，由工厂创建新 Context；原设置文件不被自动改写 |
| 代码加载已尝试 | 无论成功、失败还是取消，RequiresRestart 保持 true，不允许同一进程“回滚 DLL”再加载 |

入口 Exit 的失败或仍有池注册时，不强制释放其可能仍在使用的配置或场景；PoolService.Dispose 内部会 force 销毁，启动器只对已经为空的注册表调用它，不将强制销毁当作实例已经安全退出。自定义等待者/Provider 必须遵守主线程与取消契约，不能无限不返回；等待外部 Enter/Exit 或初始化时不通过超时强行卸载存活依赖。不可在 Enter 内 await 自己这次启动的 Stop（自等待），可以发出停止请求后返回。

State 和 Stage/Progress 用于显示当前状态，进度是固定阶段进度，不代表下载字节比例或耗时预测；FailureStage 与 LastError 保留启动失败的位置与原因，即使清理随后进行也不丢原阶段。启动与清理都失败时保留 AggregateException。公开入口主线程断言沿用文档 18。

## 3. 包与适配边界

- NowFrameWork.Boot：App/Assets/Settings 的普通 C# 装配控制，引用 Core、Unity、Assets、UniTask，不引用 Addressables/HybridCLR。
- NowFrameWork.Boot.Addressables：包含 BootFlow MonoBehaviour 与真实 Provider 装配。旧 BootFlow.cs 移入 Addressables 子目录，保留原 .meta GUID；不会修改既有场景里的脚本身份。
- NowFrameWork.Boot.HybridCLR：独立可选组件 HybridClrBootLoader，只有这个子程序集引用 HybridCLR.Runtime。未安装插件的消费工程不导入这个适配目录；Core/Unity 包仍独立可用。

HybridClrBootLoader 使用资源租约读取元数据与 DLL，每份字节使用后归还；按清单顺序补 AOT 元数据，再 Assembly.Load。元数据非 OK（包括其它路径已经加载）即报错，不假定重复补充的是兼容版本。组件有进程级防重复标志，并检查程序集名称。正式的兼容清单、签名、缓存版本选择、IL2CPP 裁剪与泛型补充实验仍由发布验证单独覆盖，不能由这个加载样例代替。

BootFlow 默认不启用代码热更，编辑器也拒绝真实 HybridCLR 加载；Editor 用假加载器验证生命周期，在 Player 验证真实 DLL。需要代码热更时添加 HybridClrBootLoader 组件并拖到 Code Loader，配置 DLL 地址、程序集名及项目生成的 AOT 元数据地址列表；不要同时挂旧 LoadDllScript。

## 4. 如何运行

在菜单 NowFrameWork → Tests → Boot → 创建启动整合场景，生成 Assets/Scenes/BootIntegration.scene（已有文件不覆盖）。打开并 Play；场景只带 BootFlow，不同时放置 GameBootstrap 或旧 DLL 测试入口。屏幕显示状态、阶段、进度、重试/停止和失败原因。

默认配置/场景地址为空、资源更新/代码热更关闭，进入占位入口后维持 Running。要验证自己的必需配置，填写其 Addressables 地址；故意填写不存在地址应进入失败并清理，不能当作可选配置吞掉。启动策略在当前 Session 中固定，修改 Inspector 后重新进入 Play 来采用新策略。

项目要退出时先 await BootFlow.Stop（或 BootSession.StopAsync），确认成功后再销毁 Boot 根对象。OnDestroy 只有异步兜底，无法保证平台进程退出时继续推进 PlayerLoop；不把关闭/保存依赖于一定收到退出事件。重试按钮只在清理完成且没有代码加载锁时可用；清理失败可重试停止，代码加载锁则提示重启进程。只要整段清理尚未完成，重试停止仍会调用幂等 ExitAsync，让项目入口有机会补做池/实例收尾。

本次新增场景不改已有 Build Settings、资源热更 Player 或独立存储 Player。横竖屏、分辨率、安全区、2D/3D、相机与业务布局仍由接入项目决定。

## 5. 验证与后续

团结 1.8.2 全量 EditMode **279/279 通过，0 失败、0 跳过**；本轮新增 **23 项**启动整合用例。报告见 [归档证据](../evidence/2026-11-16-boot-integration/README.md)，本地原始日志在 `Build/BootIntegration-20261116/editmode-verified.log`。新场景已通过编辑器菜单命令生成；未自动进入 Play。新增用例验证真实 App、模块、ConfigService、AssetService 与文件设置的组合；Provider、场景失败点、代码加载器和项目入口用可控替身注入。覆盖成功保持资源、失败清理、普通重试、启动取消、并发停止、备份恢复/未来 schema、更新顺序、代码不可回滚以及晚到资源的收尾。

本阶段尚未对新 Boot 场景重新构建 Player，实际 Addressables 目录、场景切换及 HybridCLR 元数据/DLL 在新流程下的端到端验收仍待执行。文档 17/20 既有 Windows 资源与存储证据不等价于新 Boot 流程已经通过 Native 验证。Android/iOS 真机继续按用户安排后置。

后续验证（2026-11-17）：UI-1 接入的最小普通 BootSession 已在独立 Windows Mono Player 跑通（基础资源初始化 → 项目 UI Scope → 100 次开关 → 退出及故障回滚），见 [25](25-UI最小流程与接入.md)。这不覆盖本节可选配置、场景、资源更新和 HybridCLR 的全部 Player 组合。

后续验证（2026-11-19）：主工程 `BootIntegration.scene` 的 Windows x64 IL2CPP Development Player 通过了普通 AOT 异常路径验收。使用本地 Addressables，覆盖了必需配置缺失后重启、启动中取消并重启、Provider 释放失败后再次停止。组合远端更新和真实 HybridCLR 加载留待发布验收。 [Boot Windows 验收](../evidence/2026-11-19-boot-windows/README.md)
