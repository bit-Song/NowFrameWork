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
- 本次 UI 冒烟未覆盖输入模块。Android 系统返回键和触屏摇杆待真机验收。

## 5 后续真机待测

2026-11-23 登记，本轮暂缓。上述 Android UI 冒烟结论只适用于当时的场景和包体。

- [ ] **Android 系统返回键**。有弹窗时先关弹窗，再返回上一页。切后台前仍在等待的返回请求不会在恢复后触发退出兜底。
- [ ] **双指摇杆**。第一根手指控制方向，第二根手指在摇杆区域按下、拖动或抬起时，方向仍由第一根手指控制。第一根手指松开后归零。
- [ ] **音频前后台**。播放循环 BGM 与音效时切后台再恢复，确认暂停与续播符合预期，关闭模块后声源与音频租约收尾完成。
- [ ] **证据归档**。补充设备型号、Android 版本、CPU 架构、构建类型和对应的 logcat。
