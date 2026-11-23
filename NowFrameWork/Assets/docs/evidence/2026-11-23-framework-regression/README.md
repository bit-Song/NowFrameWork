# 2026-11-23 · 框架一键回归（本机）

> 由 `Tools/Run-FrameworkRegression.ps1` 生成；原始日志与 NUnit XML 保留在 `NowFrameWork/Build/FrameworkRegression/<RunName>/`（不入库）。

## 运行信息

| 项 | 值 |
|---|---|
| RunName | `local-20261123-214758` |
| 编辑器 | `D:\Unity\TuanJie\Edite\1.8.2\2022.3.62t4\Editor\Tuanjie.exe` |
| 结论 | **passed** |

## 阶段结果

| 阶段 | 通过/总数 | 失败 | 耗时 |
|---|---|---|---|
| preflight（构建场景与框架包依赖预检） | 2/2 | 0 | 331.8s |
| main-editmode（主工程全量） | 452/452 | 0 | 40.4s |
| core-consumer（Core 独立消费工程） | 171/171 | 0 | — |
| ui-consumer（Core + Assets + UGUI 消费工程） | 299/299 | 0 | — |

## 本轮发现并修复的问题

| 问题 | 说明 |
|---|---|
| `Run-FrameworkRegression.ps1` 在只有 Windows PowerShell 5.1 的机器上无法解析 | 脚本为 UTF-8 **无 BOM**，PS5.1 按 ANSI 解码中文导致语法错误；已改为带 BOM，PS5.1 / PS7 均可解析 |

## 未覆盖

- 仅 Windows 本机；CI 调度与产物上传未接入（见 [15](../05-进度与评审/15-进度与下一步.md)）；
- Android / iOS 与真实 HTTP 下载、断网、坏文件、强制终止等场景仍待验。
