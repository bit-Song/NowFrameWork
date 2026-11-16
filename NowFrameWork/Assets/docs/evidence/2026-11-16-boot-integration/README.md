# Boot 启动整合验证证据（2026-11-16）

- 团结 1.8.2 / 2022.3.62t4，全量 EditMode 279/279 通过，0 失败、0 跳过；新增 BootSessionTests 23 项。
- editmode.xml：原始 NUnit 报告，XML 按原始字节保存。
- fingerprints.json：报告 SHA256 及本轮源码/程序集声明/生成场景的 UTF-8/LF 指纹。包含启动样例与可选插件适配，不代表插件已在 Player 实际运行。
- Build/BootIntegration-20261116/editmode-verified.log 与 scene.log：本机编译/测试和创建场景日志，未归档完整编辑器日志。

测试使用真实 App、模块、文件存储、ConfigService、AssetService；外部 Provider、项目入口和代码加载使用可控替身。新增场景 Assets/Scenes/BootIntegration.scene 已由创建菜单生成，未自动进入 Play、未重建 Boot Player、未跑 Android/iOS。已有 Windows 资源/存储 Player 基线没有被覆盖，不能代替新 Boot 的 Player 验收。
