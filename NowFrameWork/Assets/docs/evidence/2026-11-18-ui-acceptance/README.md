# UI 框架验收

2026-11-18，团结 1.8.2 / 2022.3.62t4。主工程的 `UIWindows.scene` 与 `UITween.scene` 在 Play 模式和 Windows x64 IL2CPP Player 中均通过。

| 环境 | 配置 | 原始报告 |
|---|---|---|
| 主工程 Play 模式 | Direct3D 11，Addressables 使用 Asset Database，分别进入两个现有场景 | [UIWindows](editor-ui-windows.json) · [UITween](editor-ui-tween.json) |
| Windows x64 IL2CPP Player | Development、Low stripping、普通 AOT、本地 Addressables 内容、Direct3D 11，同进程依次运行，退出码 0 | [UIWindows](il2cpp-ui-windows.json) · [UITween](il2cpp-ui-tween.json) · [构建与退出记录](il2cpp-run.json) |
| 主工程 EditMode | 396/396 通过，0 失败、0 跳过 | [EditMode XML](main-editmode.xml) |

- 两个场景都使用实际 uGUI 射线检测命中对象，再发送合成指针点击。覆盖页面切换、双层模态、共享遮罩点击策略、焦点恢复和根页实例恢复。
- `UIWindows` 额外执行 10 次页面与弹窗循环。`UITween` 在验收时启用样例的 Settings 缓存，覆盖暂停时间下的动画反向、实例复用、新旧句柄隔离、手动清理和 15 秒空闲淘汰。
- 两个场景的 Boot 停止均完成，GameEntry 解绑，会话、缓存、租约与待处理资源请求全部归零。

运行使用框架样例中的按钮和面板。此次输入覆盖为 uGUI 射线与合成指针事件。键盘与手柄的控件焦点导航继续按 [29 §6](../../04-测试与接入/29-UI多窗口导航与Canvas规范.md#6-键盘与手柄焦点导航) 的后续方案推进。
