# 2026-11-25 · Windows 真实 HTTP 发布验收

> 本机 Windows x64 Mono 包，本地 HTTP 服务（Python `http.server` / 自写断流服务）提供 `Build/ReleaseRecovery/fixtures`。
> 探针：`Assets/Game/ReleaseUpdateProbe.cs`（F9 下载 v2、F10 下载 v3）。

## 环境

| 项 | 值 |
|---|---|
| 客户端构建 ID | `windows-aot-v1` |
| 发布集 | v1（随包基线）、v2、v3 |
| 服务 | `http://127.0.0.1:8080` |
| 持久目录 | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\NowFrameWork\WindowsReleases` |

## 场景与结果

| # | 场景 | 操作 | 结果 |
|---|---|---|---|
| 1 | 基线启动 | 首次运行，无暂存发布 | `[Boot] 使用随包发布集` |
| 2 | 正常升级 | F9 下载 v2 → 重启 | `选中发布集 v2（试用）` → `ConfirmedManifest=v2` |
| 3 | 同 ID 覆盖 | 对已确认的 v2 再次暂存 v2 | 拒绝：`InvalidOperationException 不可替换正在使用的发布集: v2` |
| 4 | 坏文件 | v3 bundle 截断为 100B（清单保留原 SHA-256） | `InvalidDataException 资源长度或 SHA-256 不匹配: release-v3.bundle`；重启仍 `v2（已确认）` |
| 5 | 传输中断 | 断流服务只发 100/1192 字节后关闭连接 | 下载失败；`releases/` 无 v3；`Confirmed=v2` |
| 6 | 恢复后重试 | 恢复完整 v3 → F10 → 重启 | `选中发布集 v3（试用）` → 再启动 `v3（已确认）` |

## 状态变化（`settings-release-state.nwf`）

| 阶段 | Confirmed | Trial | Pending |
|---|---|---|---|
| 基线 | 空 | 空 | 空 |
| V2 试用中 | 空 | v2 | 空 |
| V2 确认后 | v2 | 空 | 空 |
| 坏文件失败后 | v2 | 空 | 空 |
| V3 试用中 | v2 | v3 | 空 |
| V3 确认后 | **v3** | 空 | 空 |

## 关键日志

```
[Boot] 使用随包发布集
[ReleaseProbe] 开始 http://127.0.0.1:8080/v2/release.json
[ReleaseProbe] 暂存完成: v2
[Boot] 选中发布集 v2（试用）
[Boot] 选中发布集 v2（已确认）
[Boot] 选中发布集 v3（试用）
[Boot] 选中发布集 v3（已确认）
```

## 保护行为

- **同 ID 不可覆盖**：暂存与当前确认版本相同 ID 会被拒绝，避免自我覆盖导致状态不一致；重发内容必须使用新 ID。
- **失败不落盘**：长度或 SHA-256 不匹配、传输中断都不会写入 `releases/`，`Confirmed` 保持不变。
- **确认后才持久**：只有 Boot 进入 Running 才把试用版写为 `Confirmed`。

## 未覆盖

- **强制终止 Player**：V3 写入试用后、Boot 进入 Running 前杀进程，重启应回到 v2。启动窗口很短，需要脚本化计时；PlayMode 用例已覆盖该回退语义。
- iOS / Android 的同类发布流程未验。
