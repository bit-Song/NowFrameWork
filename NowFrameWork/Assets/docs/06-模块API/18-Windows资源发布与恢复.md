# Windows 普通 AOT 资源发布与恢复

Windows x64 的 `BootFlow` 可以选配 `WindowsResourceReleases`。将组件赋给 BootFlow 的 Resource Releases 字段，并关闭旧的 Check Resource Updates 和 Load Code。未启用该组件的入口沿用现有 Boot 与更新配置。

## 版本流程

1. 发布目录包含 `release.json`、独立 Catalog 和对应 Bundle。清单记录发布 ID、平台、架构、客户端构建 ID、框架契约版本、Catalog ID、随包代码标识以及存档读写版本。每个文件记录相对路径、长度和 SHA-256。
2. `StageFromServerAsync(manifestUri, ct)` 从清单所在目录流式下载。离线样例可用 `StageFromDirectoryAsync(directory, ct)`。自定义传输可直接使用 `StageAsync(manifest, copy, ct)`。先校验兼容条件，再下载到暂存目录；所有文件通过检查后才提交待试用记录。
3. 下次进程启动时，Boot 先选择发布集并写入试用状态，然后初始化对应的 Addressables Provider。资源与附加场景使用本次选中的 Catalog。Boot 进入 `Running` 后才确认新版。
4. 新版在确认前失败或退出时，再启动会回到已知可用版本。校验发现已知可用目录损坏时使用随包内容。启动试用期间失败，需要重启进程才能重新选择。

`WindowsResourceReleases` 默认用 `Application.persistentDataPath/WindowsReleases` 保存发布内容和选择状态。路径由单个 Boot 入口管理。发布 ID 对应一套固定内容，重发内容时使用新 ID。SHA-256 用于检测文件损坏，发布服务负责提供可信的清单。

## Catalog 构建约定

可更新分组的运行时 Load Path 使用 `{NowFrameWork.ReleaseRoot}/...`，保留该占位符进入 Catalog。发布集应包含本次所需的 Bundle 及依赖。Provider 会拒绝调用 Catalog 中未在已校验文件列表里的 Bundle。随包 Catalog 关闭启动时自动更新，作为离线兜底。

当前发布接入限定普通 AOT，清单的代码集使用 `builtin`。资源通过框架资源和场景服务访问。后续接入 HybridCLR 时，将 DLL、AOT 元数据和对应 Catalog 纳入同一个发布集。

## 验收样例

编辑器菜单「NowFrameWork / Tests / 生成 Windows 发布恢复样例」在 `Build/ReleaseRecovery/fixtures` 生成 V1、V2。两者可用同一个键 `ReleaseProbe/Config` 读取各自的版本文案。回归类 `ReleaseStorePlayTests` 覆盖不兼容、校验错误、传输取消、磁盘损坏、试用未确认及正常升级。运行真实 Catalog 用例时给测试进程设置 `NWF_WINDOWS_RELEASE_FIXTURES`，指向上述目录。

[资源更新 API](13-资源更新与代码加载.md) · [Boot API](01-Boot与服务入口.md) · [模块索引](README.md)

## Windows 端到端待测

2026-11-23 登记，本轮暂缓。现有报告已覆盖 Windows IL2CPP 中的真实 Catalog 与 Bundle 选版、测试传输取消，以及重新打开持久状态后的试用回退。

- [ ] **实际 HTTP 发布**。从本地 HTTP 服务下载 V2，验证正常升级、传输中断和服务端返回损坏文件。失败后重启应继续使用 V1，服务恢复后应能重新下载 V2。
- [ ] **强制终止 Player**。V2 写入试用状态后、Boot 进入 `Running` 前终止进程。重启应选择 V1。重新完整试用 V2 并确认后，再启动应继续选择 V2。
- [ ] **归档证据**。记录 HTTP 请求、Player 启动选版和确认日志，以及终止前后持久状态的变化。
