# 2026-11-25 · PlayMode 在 batchmode 下的可用性评估

> 结论：**不把 PlayMode 纳入 CI**。batchmode 下 PlayMode 用例大量失败，本地化单条即使放宽到 120 秒仍超时。

## 环境

| 项 | 值 |
|---|---|
| 命令 | `Tuanjie.exe -batchmode -projectPath <项目> -runTests -testPlatform PlayMode` |
| 图形 | 未传 `-nographics`（默认） |

## 全量结果

| 指标 | 值 |
|---|---|
| 用例数 | 17 |
| 失败（含套件汇总） | 14 |

失败用例：

- `CharacterUsesNewBindingImmediatelyAndAfterReopening`
- `TweenNavigatesAndReleases`
- `WindowsScene_NativeKeyboardAndGamepadMoveAndRestoreModalFocus`
- `SwitchUpdatesOpenPanelAndReopenedSettingsRestoreLocale`（本地化专项）

## 本地化专项单跑

| 尝试 | 超时 | 结果 |
|---|---|---|
| 1 | 30s | Failed（`OperationCanceledException`，`WaitUntil` 未满足） |
| 2 | 120s | Failed（同样超时） |

说明不是"启动慢"，而是 batchmode 下 UI 场景/Boot/Addressables 的组合没有走到 `LocalizedSampleText` 出现。

## 决策

| 选项 | 决定 |
|---|---|
| PlayMode 进 CI | ❌ 不做 |
| PlayMode 本地交互跑 | ✅ 保留，EditMode 门禁之外的补充验证 |
| 后续替代 | 可考虑 Windows Player 冒烟（启动一次、检查 `Boot.Completed` 日志），成本更高，另立任务 |

## 影响

- CI 仍覆盖：preflight、主工程 EditMode、Core 消费、UI 消费；
- 本地化行为已由 Android/Windows 真机复测覆盖，PlayMode 仅作本地回归补充。
