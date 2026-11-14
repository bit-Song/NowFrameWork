# 05 资源与热更技术方案

> v0.4 ｜ 契约：[02](02-架构分层与依赖规则.md) ｜ 验证：[10](10-首版范围与开工评审.md)

## 1 资源边界

业务只使用 AssetRef、IAssetService 与 AssetLease，不直接引用 Addressables。首个 Provider 使用当前工程声明的 Addressables 1.22.4-t1，在 M0 验证；不承诺 Unity 官方工程直接使用团结适配版本。替换 Provider 时还需验证构建、catalog、缓存与更新语义，不把接口稳定等同于迁移零成本。

资源更新与代码更新解耦，本地资源完整时可离线运行。启动错误面板由 Boot 直接引用，不能依赖失败中的 Provider 初始化。

## 2 租约 引用与取消

缓存键至少包含发布快照、地址和请求类型，防止不同资源版本共用旧记录。记录状态为 Queued → Loading → Loaded/Failed → Released；重试使用明确的尝试编号，旧回调不能覆盖新尝试。

| 对象 | 职责 |
|---|---|
| 底层记录 | 持有 Provider 操作、等待者、有效租约数、状态与快照标识 |
| 等待者 | 一次申请，持独立取消状态；成功时移出等待集合并创建租约 |
| AssetLease | 一次成功申请的 sealed 对象，Dispose 幂等，只释放自己的份额 |
| 缓存保留 | 以显式缓存租约计数，声明应用或场景归属与淘汰策略 |

所有记录状态在 Unity 主线程或单一受控调度点转换；后台取消仅投递状态变更，不直接操作 Unity 对象。成功与取消竞争只有一个终态。取消不产生业务租约；已成功返回后由调用者释放租约。

排队取消可移除请求。加载中取消只结束该等待者，其他等待者继续。全部等待者取消且没有租约时，按 Provider 支持程度取消或登记完成后释放；即便操作最终完成，也不再交付给已取消调用者。失败操作也要回收底层句柄。Provider 原生加载不能被普遍假定为支持 CancellationToken 中断。

同址双请求分别得到两个租约。释放 A 不使 B 失效；重复释放 A 不改变 B 的计数。诊断计数需区分等待者、业务租约、缓存保留与底层操作，不笼统要求任一取消后全部归零。

## 3 预加载 场景与内存

预加载返回可释放的批次/范围租约，归属可查询；不提供所有权不明的永久预加载。并发上限与优先级可配置，移动端按实测调整，不将固定并发数作为全部设备最优值。异步完成不等于所有反序列化和实例化都在后台线程执行。

场景持有专用租约，区分下载、加载、激活、卸载；未激活场景的等待与取消需单独验证。卸载流程先停止业务任务和订阅、销毁实例，再释放它们持有的资源，最后释放场景及其附加资源。场景提供者负责处理激活对加载队列的影响，不因取消等待而留下永久未激活操作。

Boot 常驻，场景数量上限由项目配置，不写死仅两个场景。低内存时先销毁闲置 UI/对象并释放缓存租约，再淘汰预加载；有效使用者持有的资源不强制卸载。内存是否回落按采样与 Bundle 依赖分析，不承诺引用归零就立即等量回落。

## 4 地址 分组与变体

| 内容 | 策略 |
|---|---|
| Boot 与应急 UI | Build Settings 首场景直接引用最小本地资产 |
| 必需首屏资源 | 随包本地资源，离线可用 |
| 常用资源 | 按实际依赖共享，分析 Bundle 大小与下载放大 |
| 关卡资源 | 按加载与更新粒度分组，各平台独立构建 |
| 语言与画质 | 显式选择表，将逻辑 ID + 语言/档位解析为唯一地址 |
| 可选热更字节 | 与兼容发布集绑定，随包兜底与远端输入分开管理 |

地址稳定，变更需要兼容映射。label 仅用于分组/筛选，不提供自动语言或画质替换；不再要求 Addressables Variant。多标签匹配须通过选择表消除歧义，缺失按默认语言/档位回退。

重复依赖按资源 GUID 与构建报告判断，不按同名文件判定。共享分组不是越大越好，须评估移动端内存、首包和更新放大，允许有记录且预算内的重复。

## 5 普通与代码热更构建

普通模式：同一份业务随包 AOT 编译，配置中不包含 HybridCLR 包、生成步骤和运行时引用。更新资源时仍需与随包代码及 schema 兼容。

热更模式：独立构建配置启用 HybridCLR 适配，启动器不得静态引用热更业务。按平台生成热更代码、裁剪保留配置、桥接和 AOT 补充元数据，再构建对应资源与 Player；实际命令、输出路径和先后依赖以锁定工具的生成结果记录，不沿用未经核对的固定路径。

运行时先选定完整兼容发布集，初始化资源 Provider，加载匹配的 AOT 元数据，再按依赖序加载程序集并取得 IGameEntry。Unity 对象创建和入口初始化在主线程进行；不默认用后台 Assembly.Load 优化。释放字节资源租约不代表程序集自身占用随之回收。

**热更构建的四条硬基线**（2026-11-14 实测得出，缺一条都会出现"Editor 里能跑、真包加载失败"）：

| 配置 / 流程 | 值 | 不满足时的表现 |
|---|---|---|
| Api Compatibility Level | **.NET Framework** | `TypeLoadException: Could not load type … from assembly 'netstandard'` |
| Managed Stripping Level | **Minimal** 或更保守 | `TypeLoadException … from assembly 'mscorlib'`（类型被裁掉） |
| link.xml | 热更代码每新增/删除一次对 AOT 类型的引用，都要重跑 `HybridCLR/Generate/All` | 运行时找不到被裁剪的类型（实例：`AsyncTaskMethodBuilder`） |
| 程序集划分 | 热更 asmdef 关闭 `Auto Referenced`、登记进 Hot Update Assemblies、AOT 侧不引用 | 热更 dll 被当 AOT 编进包，补丁永远不生效 |
| AOT 补充元数据 | 取自当前 Player 的 `AssembliesPostIl2CppStrip`，与包严格同源；必须在 `Assembly.Load` **之前**逐个 `LoadMetadataForAOTAssembly` | AOT 泛型/反射入口报 `MissingMethodException: AOT generic method not instantiated in <程序集>` |

补充事实（2026-11-14 实测）：普通 IL2CPP Player 里**不存在** `Assembly.Load(byte[])` 对应的内部调用（`AppDomain::LoadAssemblyRaw`），调用会抛 `NotSupportedException`。也就是说"热更"不是配一个开关，而是必须安装 HybridCLR 运行时；这同时证明普通模式与热更模式的产物确实是两套。

判定原则：**热更代码里用到、而 AOT 侧没用到的类型和方法，都会在 IL2CPP 裁剪阶段被删除。** 每次热更代码变更后都要"重跑 `Generate/All` → 重出 Player"；只在 Editor Play Mode 里通过不能作为验收证据。

iOS 默认保留普通模式；技术能力与发布策略分开记录，代码更新启用前按渠道要求评估，参见 [Apple App Review Guidelines](https://developer.apple.com/app-store/review/guidelines/)。

## 6 发布集与回退

清单至少包含：Platform、Architecture、ClientBuildId、FrameworkContractVersion、ReleaseId、ResourceCatalogId、CodeSetId（普通模式为随包标识）、SaveSchemaReadRange、SaveSchemaWriteVersion，以及文件长度与 SHA-256。启用代码更新时还记录元数据集标识和程序集依赖顺序。客户端兼容校验不能只比较 minAppVersion。

下载至 staging → 校验全部内容与兼容性 → 原子切换 active 发布指针。保留 last-known-good 与随包集；代码和资源作为一套恢复，不把新 catalog 配旧 DLL。启动成功达到明确检查点后才将 active 晋升为已知可用版本。哈希用于完整性，若需要真实性保护另定义签名与信任根；不把哈希描述为来源认证。

| 失败点 | 行为 |
|---|---|
| 下载或校验失败 | 不提交 staging，继续使用兼容本地集 |
| 代码加载前发现不兼容 | 拒绝该集并选择已知可用或随包集 |
| 已开始程序集加载后失败 | 不在当前进程加载同名旧程序集；显示本地恢复提示，持久化失败标记，下次启动选择兼容旧集 |
| 新版启动失败或持续崩溃 | 启动尝试标记未完成，下次进入安全恢复；限制重试避免循环 |
| 存档由新版写出 | 按读写 schema 范围判定；旧版不能覆盖不能读取的存档，可选只读、备份或阻止回退 |

安全恢复入口须能在移动端使用随包策略或持久化失败标记触发，不只依赖 PC 命令行。保留版本数量按磁盘预算决定，清理前不得删除 active、已知可用及其存档兼容所需内容。

## 7 资源版本与风险

每个 Player 归档编辑器、Provider 版本、构建配置、catalog、Bundle 和 content state。升级编辑器/Addressables 按全量 Player 及内容重建流程处理，禁止未验证地跨基线复用旧 content state。依赖升级后重跑三平台基础矩阵与适用热更用例。

风险按引擎构建、下载/catalog、资源生命周期、存档、代码裁剪与渠道策略分别登记。代码加载或裁剪问题不自动触发换 YooAsset；如需更换，先通过最小实验定位，并更新 ADR、接口适配与工期。
