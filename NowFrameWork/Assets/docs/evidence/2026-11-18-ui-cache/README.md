# UI 面板缓存验证

2026-11-18，团结 1.8.2 / 2022.3.62t4。面板缓存阶段的独立消费工程 EditMode 388/388，0 失败、0 跳过。

- [独立消费工程 EditMode 原始报告](editmode.xml)
- [Windows Mono Player 原始结果](native-result.json)

Player 使用真实 Addressables 和 DOTween，覆盖实例及租约复用、新参数与句柄、手动清理、暂停时间下自动到期和停止后租约归零。接入与编辑器试用步骤见 [31 UI 面板缓存与接入](../../04-测试与接入/31-UI面板缓存与接入.md)。独立测试沿用 [动画阶段的消费工程](../2026-11-18-ui-tween/README.md)。
