# 证据归档

自动化报告直接放在 `Build/` 下（不入库、可能被清理）；这里保存需要长期保留的归档副本。

| 目录 | 内容 |
|---|---|
| [`2026-11-18-ui-acceptance/`](2026-11-18-ui-acceptance/README.md) | 主工程 Play 与 Windows x64 IL2CPP 的 UI 样例验收，附主工程 EditMode 396/396 |
| `2026-11-18-failure-path-fixes/` | 退页与资源释放失败后的重试回归（主工程和独立消费工程均 392/392） |
| `2026-11-18-ui-cache/` | 面板缓存阶段（独立消费工程 388/388，Windows Mono 缓存与租约验证） |
| `2026-11-18-ui-tween/` | 面板独立动画与 DOTween 可选适配（无插件基础 364；完整 377；Windows Mono 动画反向/循环/退出） |
| `2026-11-18-ui-windows/` | 页面历史、共享模态、返回、焦点与 Canvas 排序（EditMode 361，UI 54；Windows Mono 真实输入射线与 10 次完整循环） |
| `2026-11-18-gameentry/` | GameEntry 生命周期与 Boot/业务入口接入（EditMode 343，其中 GameEntry 12；Windows Mono Player 100 次开关与停止/重启） |
| `2026-11-17-ui-integration/` | UI 真实工厂、反向/生命周期与模块接入（EditMode 330、UI 35，Windows Mono Player 100 次循环及失败收尾） |
| `2026-11-17-ui-fixes/` | UI 所有权、关闭失败重试与取消隔离（EditMode 301，其中 UI 22，全部通过） |
| `2026-11-16-boot-integration/` | Boot 启动整合回归（EditMode 279，全部通过） |
| `2026-11-16-settings/` | 存储与设置回归（EditMode 256，全部通过） |
| `2026-11-16-thread-guard/` | 主线程断言回归（EditMode 206，全部通过） |
| `2026-11-16-pc-regression/` | PC 资源/热更 Player 回归（EditMode 185 + 七轮 Player 运行） |
| `2026-11-16-storage-player/` | 独立 Windows IL2CPP 存储验证（10 个进程阶段） |

约定：

1. 每份归档包含 `README.md`（复现步骤与结论）+ 原始 XML / 日志；
2. 报告数值以 XML 的 `test-case` 节点计数为准，不看控制台最后一行；
3. 新增一轮回归时，目录名用 `<日期>-<主题>`，并在此表登记。
