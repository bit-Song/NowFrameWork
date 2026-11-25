# 2026-11-25 · 框架一键回归（HFSM 与 UI 后复测）

> 由 `Tools/Run-FrameworkRegression.ps1` 生成；原始日志与 NUnit XML 在 `NowFrameWork/Build/FrameworkRegression/<RunName>/`（不入库）。

## 运行信息

| 项 | 值 |
|---|---|
| RunName | `p0-20261125-084312` |
| 编辑器 | Tuanjie 1.8.2（2022.3.62t4） |
| 结论 | **passed** |

## 阶段结果

| 阶段 | 通过/总数 | 失败 |
|---|---|---|
| preflight（构建场景与框架包依赖） | 2/2 | 0 |
| main-editmode（主工程全量） | **477/477** | 0 |
| core-consumer（Core 独立消费工程） | 191/191 | 0 |
| ui-consumer（Core + Assets + UGUI 消费工程） | 322/322 | 0 |

## 覆盖的新增内容

- Core 层级状态机（HFSM）及其 PlayMode 用例；
- UI 方向导航、低内存闲置缓存回收；
- 改键对 2D 复合单方向的支持与同 Action Map 冲突提示；
- `com.now.nowframework.input.inputsystem` 适配包接入后的依赖解析（preflight 通过）。

## 未覆盖

- Android / iOS 真机（本地化、双指摇杆、系统返回）；
- Windows 真实 HTTP 发布、断网、坏文件与强制终止恢复；
- 15 号列出的两个方向导航缺陷（禁用 CanvasGroup 下选中、禁用当前按钮后重建丢焦点）仍待修。
