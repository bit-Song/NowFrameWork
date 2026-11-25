# 2026-11-25 · GitHub Actions 首次回归通过

> 自托管 runner `tuanjie-win`（`D:\Other\actions-runner`），工作流 `.github/workflows/framework-regression.yml`。
> 运行 #3，提交 `f40ceb8`，耗时 **10 分 4 秒**，全部步骤绿色。

## 阶段结果

| 阶段 | 通过/总数 | 失败 | 耗时 |
|---|---|---|---|
| preflight（构建场景与框架包依赖） | 2/2 | 0 | 438.0s |
| main-editmode（主工程） | **479/479** | 0 | 59.6s |
| core-consumer（Core 独立消费工程） | 191/191 | 0 | 33.0s |
| ui-consumer（Core + Assets + UGUI 消费工程） | 324/324 | 0 | 36.8s |

## 与本地基线的差异

| 项 | 本地 | CI | 原因 |
|---|---|---|---|
| 主工程 EditMode | 481 | **479** | CI 无图形设备，排除 2 条 `Category("RequiresGraphicsDevice")`（`FsmTraceEditorTests`） |
| UI 消费工程 | 322 → 324 | **324** | 322 为新增 2 条方向导航用例之前的数字；两处已一致 |

## 接入过程中修复的问题

| # | 问题 | 修复 |
|---|---|---|
| 1 | 工作流前两次 33 秒失败，无运行目录 | 步骤先用 `shell: powershell`，GitHub 生成的临时 `.ps1` 被机器执行策略拒绝（`PSSecurityException`），脚本内 `Set-ExecutionPolicy` 来不及执行 | 步骤改 `shell: cmd`，显式 `powershell -ExecutionPolicy Bypass -File Tools\Ci-RunRegression.ps1`；诊断/转录/汇总移入 `Tools/Ci-*.ps1`（UTF-8 BOM） |
| 2 | 失败时看不到日志 | artifact 只收 `Build/FrameworkRegression/**` | `Ci-RunRegression.ps1` 用 `Start-Transcript` 记录整步输出，`finally` 中复制为 `Build/FrameworkRegression/ci-step.log`，随 artifact 上传 |

## 证据

- `run.json`：本次四阶段计数与耗时；
- 原始日志与 NUnit XML 保留在运行 artifact `framework-regression`（runner 本机路径：`D:\Other\actions-runner\_work\NowFrameWork\NowFrameWork\NowFrameWork\Build\FrameworkRegression\20261125-115414-65a52750\`）。

## 未完成

- **失败归档验收**：临时分支加 `Assert.Fail` 后手动运行，确认失败时 artifact 仍可下载；
- PlayMode（本地化专项）阶段尚未纳入工作流；
- 运行失败时的 Step Summary 展示未实测（成功路径已生成表格）。
