# 2026-11-25 · Windows 发布试用期强制终止验收

> 目标：V2 写入试用状态后、Boot 进入 Running 前终止进程，重启应回到已确认的 V3。
> 工具：`Tools/ReleaseKillProbe.ps1` + `Build/ReleaseProbe/NowFrameWork.exe`（Mono，探针支持 `-stageRelease`）。

## 起点

| 项 | 值 |
|---|---|
| 已确认版本 | v3 |
| 暂存版本 | v2（与确认版不同 ID，避免同 ID 拒绝覆盖） |
| HTTP 服务 | `http://127.0.0.1:8080`（fixtures） |

## 过程与结果

| 阶段 | 观察 | 结果 |
|---|---|---|
| 暂存 v2 | `Pending=v2`、`Confirmed=v3` | ✅ |
| 试用启动 | `Player-prev.log`: `[Boot] 选中发布集 v2（试用）` | ✅ |
| **检测到试用状态后强杀** | 状态：`Trial=v2`、`Confirmed=v3`（第 1 次尝试即命中） | ✅ |
| 重新启动 | `Player.log`: `[Boot] 选中发布集 v3（已确认）` | ✅ 回退到已确认版本 |
| 最终状态 | `Confirmed=v3`、`Trial` 与 `Pending` 清空 | ✅ |

## 判定依据

- 强杀发生在 `TrialManifest=v2` 写入之后（状态文件可见），说明试用已生效；
- 重启选择 v3 而非 v2，说明**未确认的试用被丢弃**；
- `TrialManifest` 被清空，说明回退路径正常收尾。

## 工具变更

| 文件 | 变更 |
|---|---|
| `Assets/Game/ReleaseUpdateProbe.cs` | 新增命令行入口 `-stageRelease <id>`，不依赖合成键盘事件 |
| `Assets/Aot/Editor/ReleaseProbeBuild.cs` | 新增 Mono 构建入口 `NowFrameWork/Tests/构建发布验收 Player (Mono)` |
| `Tools/ReleaseKillProbe.ps1` | 自动暂存 → 轮询试用状态 → 强杀 → 重启验证回退 |

## 备注

- 合成按键（`SendKeys F9`）对 Input System 无效，因此改用命令行参数触发下载；
- 试用窗口取决于 Boot 启动速度，脚本以 10ms 轮询状态文件并在命中后立即 `Stop-Process -Force`；本次一次命中。
