# Windows 存储 Player 证据（2026-11-16）

团结 1.8.2 / 2022.3.62t4，Windows x64 IL2CPP Development、Low stripping；10 个独立进程阶段通过，所有退出码为 0。每轮使用新的隔离目录，本次归档最后一次 20261116-commit-review。阶段内容及运行方式见 [20](../../04-测试与接入/20-存储Player验证.md)。

- 各 phase.json：原始 Player 报告，包含 backend、development、finished、passed 与逐项检查名。
- run.json：运行器记录的各阶段退出码、报告路径、启动前代码指纹，以及全部阶段通过且运行后指纹一致时的最终完成标记。里面的绝对 report 路径是运行时原位置；归档副本就在本目录。
- consumer-manifest.json / consumer-packages-lock.json：普通消费工程只安装 Core、Unity 适配、UniTask 和 JSON/IMGUI 模块；文件路径为原机位置，没有在主项目卸载 Addressables/HybridCLR。
- fingerprints.json：上述原始 JSON 字节指纹、实际构建模板/框架源码的 UTF-8/LF 指纹、新 Player 与旧 PC 资源回归 Player 的代码指纹。

完整日志、故障注入后的数据和二进制位于忽略的 Build/StorageRegression。没有把测试夹具的数据或 Player 产物纳入框架包。此证据不替代 Android/iOS、Pause、真实进程强杀或断电验证。
