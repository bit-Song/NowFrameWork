# 20 · 存储 Player 验证

> 2026-11-16：独立 Windows x64 IL2CPP Development Player，10 个独立进程阶段全部通过。验证代码与运行脚本位于仓库 Tools；框架运行时代码没有在本步骤变更。

## 1. 为什么使用独立工程

主工程仍保留 Addressables 与 HybridCLR。本次在被忽略的 Build/StorageRegression/Consumer 生成另一个消费工程，只安装 Core、Unity 适配包、UniTask 和 Unity JSON/IMGUI 内置模块；不安装 Addressables/HybridCLR，也不复用主项目的 HybridCLR 本地 IL2CPP 路径。验证存储机制可以在普通 AOT 项目独立运行，不改变主项目的插件选择。

新 Player 输出到 Build/StorageRegression/Player/StorageRegression.exe。原 Build/PCRegression/Player 的 EXE、GameAssembly.dll、TuanjiePlayer.dll 指纹仍与文档 17 基线相同；不能用相同的引擎启动 EXE 指纹代替整个程序的代码身份，因此新旧 Player 都核对 GameAssembly.dll。

消费工程使用相同团结 1.8.2（2022.3.62t4），直接引用本仓库的 Core/Unity 包，构建选用 IL2CPP、Development、Low stripping。每次进程启动确认主线程已经登记，并检查当前加载的程序集没有 HybridCLR/Addressables；独立 manifest 与锁文件也归档为依赖证据。

## 2. 运行与复现

1. 在仓库根运行 [准备脚本](../../../../Tools/Prepare-StorageRegression.ps1)：`powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Prepare-StorageRegression.ps1`。它使用主项目缓存中的 UniTask，不访问网络；找不到唯一缓存时传 -UniTaskPath 指定本地 UniTask 包目录。已有目标目录必须带隔离标记才允许更新模板。
2. 用同版本编辑器构建隔离工程；主项目无需切换构建配置。不能同时用第二个编辑器打开这个隔离工程。
3. 运行 [多进程脚本](../../../../Tools/Run-StorageRegression.ps1)：`powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Run-StorageRegression.ps1`。脚本启动测试程序，不需要服务器；不要直接双击 EXE，它需要脚本提供 phase、数据目录和报告参数。

构建命令示例（当前机器路径；这是单独的 PowerShell 进程，不修改全局执行策略）：

```powershell
$buildArgs = @(
    '-batchmode', '-nographics', '-quit',
    '-projectPath', 'D:\UnityProject\TMP\NowFrameWork\Build\StorageRegression\Consumer',
    '-buildTarget', 'Win64',
    '-executeMethod', 'NowFrameWork.Validation.Editor.StorageProbeBuild.Build',
    '-logFile', 'D:\UnityProject\TMP\NowFrameWork\Build\StorageRegression\build.log'
)
Start-Process -FilePath 'D:\UnityEditor\TuanJie\Editor\Tuanjie.exe' -ArgumentList $buildArgs -WindowStyle Hidden -Wait
```

日志出现 [StorageBuild] PASS 且生成 EXE 后再执行运行脚本。准备模板与构建过程由你显式发起；运行脚本只运行已经构建的 Player，不自动重新出包。修改框架或模板后，先准备模板、重新构建，再运行测试。

每轮创建新的 Evidence/<RunName>/Data。10 个进程共享这份隔离数据，不共享其它轮次数据。故障实验要求目录内有专用标记，只修改固定的 player-probe 文件；不清理用户存档，也不自动删除失败现场。已有报告目录拒绝复用。最后一阶段会故意留下两份损坏文件，供核对保留现场行为。

## 3. 10 个阶段及实际结果

| 阶段 | 验证 | 结果 |
|---|---|---|
| seed | 首次创建默认内存对象，不自动写盘；两次 Save 生成主文件与上一版备份 | 通过 |
| read | 新进程从主文件读到 Volume=75 和中文字段 | 通过 |
| recover | 主文件截断后报告备份恢复，读到上一版 25；保存修复后备份字节不变 | 通过 |
| read-repaired | 新进程读取修复后的 60 | 通过 |
| migrate | schema 1 → 2，只在显式 Save 后提交；旧版完整文件成为备份 | 通过 |
| read-v2 | 新进程不再迁移，读到 schema 2 和迁移生成的字段 | 通过 |
| reject-old | 旧 schema codec 拒绝读写新版，主/备份字节不变 | 通过 |
| limit | 限额变小后拒绝加载，不回退旧备份，也不能 Save | 通过 |
| thread | 真实后台 Thread 调用 Save，在状态修改/IO 前由 Development 断言拦截 | 通过 |
| corrupt-both | 两份损坏时不重置默认值、不允许覆盖，保留原始故障字节 | 通过 |

最终复核轮次为 20261116-commit-review。10 个进程退出码均为 0、finished/passed 均为 true；运行前后 Player 代码指纹一致。每个 JSON 内含实际检查名；公共的 IL2CPP/主线程/可选程序集检查在各进程重复，不当作额外独立功能用例。

[归档证据](../evidence/2026-11-16-storage-player/README.md)包含各阶段原始 JSON、run.json、依赖清单和 SHA256 清单。完整构建/运行日志与故障数据保存在本地 Build/StorageRegression，不提交 Player 二进制、Library 或缓存。

## 4. 本轮边界与下一步

这次是正常进程退出与再次启动，并通过夹具注入文件损坏，不是操作系统强杀或硬件断电测试。未测 Android/iOS、移动端 Pause 提交、磁盘满/权限拒绝、大文件耗时、强裁剪和 Release Player。FileSettingsStore 没有自动暂停保存；项目仍须明确选择提交时机，不依赖一定收到退出回调。

编辑器全量 256 项基线维持文档 19 的结果；本次没有修改框架运行时或重新宣称全部测试在 Native Player 上执行。后台线程 Native 用例只覆盖存储 Save 入口，其它服务入口仍以文档 18 的 EditMode 证据为准。

按用户 2026-11-16 安排，Android/iOS 真机测试暂时后置；iOS Build Support 用户已确认补装，本轮不新增 iOS 编译通过声明。当前先完成 Windows 基线归档，再推进框架接入与装配。后续将相同消费工程扩展到移动端，验证持久化目录、重启读取与暂停前显式保存；iOS 执行前需核对 Mac/Xcode、签名和设备条件。主工程中的 Addressables/HybridCLR 资源测试仍走文档 17 的独立流程。
