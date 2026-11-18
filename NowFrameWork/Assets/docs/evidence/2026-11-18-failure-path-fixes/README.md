# 退页清理与资源释放重试回归

2026-11-18，团结 1.8.2 / 2022.3.62t4。主工程与引用同一框架源码的独立消费工程均通过 392 项 EditMode 测试，0 失败、0 跳过。

- [主工程 EditMode 原始报告](main-editmode.xml)
- [独立消费工程 EditMode 原始报告](consumer-editmode.xml)

新增 4 项回归覆盖退页清理失败后继续 Push、最后一个租约重试同一 Provider 句柄、无主句柄在同址重载前清理，以及配置服务关闭时的释放重试。

主工程重试时成功安装 `com.unity.memoryprofiler@1.1.12`。为 UI 动画场景菜单所属的编辑器程序集补上 Core 引用后，主工程完成编译和测试。复现时可在团结菜单选择 **NowFrameWork → Tests → 运行全部 EditMode 测试**，或在编辑器关闭时使用 `-batchmode -nographics -runTests -testPlatform EditMode -testResults <报告路径>`。
