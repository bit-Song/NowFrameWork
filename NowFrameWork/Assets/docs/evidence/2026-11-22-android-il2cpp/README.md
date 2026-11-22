# 2026-11-22 · Android IL2CPP 冒烟

> 状态：**用户口述通过，原始日志与截图待补**。本文件先登记结论与复现步骤，避免证据丢失。

## 1 结论

| 项 | 结果 |
|---|---|
| 平台 | Android（真机） |
| 脚本后端 | IL2CPP |
| 架构 | 待补（建议 ARM64） |
| 场景 | `UIIntegration.scene` |
| 结果 | 启动与 UI 流程正常 |
| Release 包 | **通过**（关闭 Development Build 后复测正常） |

> 在同一天先用 Mono 出包验证正常，随后切 IL2CPP 复测通过。

## 2 复现步骤

1. `File > Build Settings > Android`；
2. Player Settings：`Scripting Backend = IL2CPP`、`Target Architectures = ARM64`、`Development Build` 开启；
3. Addressables：切到 Android 后 `Build > New Build > Default Build Script`；
4. Build 出 APK，`adb install -r`；
5. 启动后按验收清单检查。

## 3 待补证据

- [ ] `adb logcat -s Unity:V` 的完整输出（重点：`Exception` / `MissingMethodException`）；
- [ ] 设备型号与 Android 版本；
- [ ] `Target Architectures` 实际勾选项；
- [ ] 启动与面板开关的截图；
- [ ] 结束时 `ActiveLeases` / `SessionCount` 的日志行。

## 4 未覆盖

- iOS（需要 Mac 与设备）；
- 长时间运行与内存压力；
- 音频在移动端后台切换的实际表现；
- 输入模块（Android 返回键）仍为未实现，见 [03 模块清单 F08](../../01-架构与规划/03-模块清单与优先级.md)。
