# 证据归档

自动化报告直接放在 `Build/` 下（不入库、可能被清理）；这里保存需要长期保留的归档副本。

| 目录 | 内容 |
|---|---|
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
