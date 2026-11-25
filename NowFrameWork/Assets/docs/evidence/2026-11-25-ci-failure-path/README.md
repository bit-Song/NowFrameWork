# 2026-11-25 · CI 失败归档验收

> 临时分支 `ci-failure-acceptance`，在 `JoystickMathTests.SecondPointerCannotMoveOrReleaseActiveJoystick` 注入 `Assert.Fail("CI failure-path acceptance")`。
> 运行 #7，结论 **failure**，8 分 27 秒；artifact `framework-regression` **17.5 MB** 正常上传。

## 验收结果

| 检查 | 期望 | 实际 |
|---|---|---|
| 工作流结论 | failure | ✅ failure |
| 失败步骤 | Run framework regression | ✅ 8m27s |
| 后续步骤 | Summarize / Upload 仍执行 | ✅ 均成功 |
| `run.json` | `status=failed`，失败阶段 `failed=1` | ✅ main-editmode 478/479 |
| NUnit XML | 含注入消息 | ✅ `CI failure-path acceptance` |
| `ci-step.log` | 存在 | ✅ 1897B |
| Artifact | 非空 | ✅ 17.5 MB |

## 过程中发现并修复的两个真实缺陷

| # | 缺陷 | 现象 | 修复 |
|---|---|---|---|
| 1 | 失败不传播 | 运行 #5 中 `run.json` 已 `failed`（478/479），但 CI 显示 **success** | `Run-FrameworkRegression.ps1` 末尾 `exit 1` 只退出被调脚本并设置 `$LASTEXITCODE`；改为 `throw`，并在 `Ci-RunRegression.ps1` 检查退出码 |
| 2 | 脚本 BOM 丢失 | 运行 #6 失败但"未生成回归目录"，无 artifact | 编辑后 BOM 丢失，PS 5.1 按 ANSI 解码中文导致字符串截断、脚本解析失败；三个脚本统一恢复 UTF-8 BOM，并在本地用 PS 5.1 语法校验 + 失败路径实测 |

## 附带改进

- `Ci-RunRegression.ps1` 在无 `RUNNER_TEMP`（本地运行）时回退系统临时目录；
- Step Summary 改为 UTF-8 无 BOM 追加，修复中文乱码；
- 转录输出复制到 `Build/FrameworkRegression/ci-step.log`，失败时随 artifact 上传。

## 未完成

- PlayMode（本地化专项）阶段仍未纳入工作流。
