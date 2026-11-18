# UI 多窗口与导航验证

日期：2026-11-18。团结 1.8.2 / 2022.3.62t4。基础提交 `3196f71`，增加共享模态呈现、UiNavigator、UiSafeArea 和项目测试入口。交付源码与验证产物指纹见 fingerprints.json。

## 结果

- [EditMode 原始报告](editmode.xml)：**361/361**，0 失败、0 跳过。其中 UI 54 项，包含本轮新增窗口/导航测试 17 项与真实模块遮罩测试 1 项；其余已有模块/Boot 279、销毁工具 3、资源键生成器 13、GameEntry 12。
- [Windows Player 原始结果](native-result.json)：passed=true。Development Mono x64，真实 Addressables 内容与 UiModule，验证页面同实例恢复、双层模态、单张共享全屏遮罩、屏幕边角真实 EventSystem 射线、点击策略、输入和有效焦点恢复、10 次完整流程，以及导航进入途中停止。停止后 ActiveLeases=0，GameEntry 已解绑。
- [实际离屏渲染截图](ui-windows.png)：确认弹窗在最上层，背景和 Inventory 被同一张遮罩压暗。截图经相机 RenderTexture/ReadPixels 获取，人工检查了布局与文字。

## 复现

使用与前一轮 [UI 夹具](../2026-11-17-ui-integration/README.md) 相同的独立消费工程组织，依赖快照见 [consumer-manifest.json](consumer-manifest.json)。Framework 包直接引用仓库源码；项目复制 Boot/Boot.Addressables、Game、Generated、编辑器生成器与测试，保留现有资源键测试。不需要 HybridCLR。

1. 执行 `-batchmode -nographics -projectPath <消费工程> -runTests -testPlatform EditMode -testResults <输出>/editmode.xml -logFile <输出>/tests.log`。新增包内测试为 UiWindowTests 与 UnityUiViewFactoryTests 的模块遮罩用例。
2. 将 [UiWindowsNativeProbe.cs.txt](UiWindowsNativeProbe.cs.txt) 放进 UIValidation.Runtime、[UiWindowsValidationBuild.cs.txt](UiWindowsValidationBuild.cs.txt) 放进 UIValidation.Editor，移除 .txt 后缀。运行时程序集引用 Game、Boot、Assets/Addressables、Unity/Core、UI、UniTask 和 UnityEngine.UI；编辑器另引用 Unity.Addressables.Editor、Unity.Addressables、Unity.InputSystem、UI.Editor 与运行时验证程序集。
3. 执行 `-batchmode -nographics -quit -projectPath <消费工程> -executeMethod UiWindowsValidationBuild.PrepareAndBuild -logFile <输出>/build.log`。调用项目菜单生成器创建 UIWindows 场景和两份 Prefab，注册 UiWindows/Window，构建 Addressables 本地内容与 Windows Player。
4. 运行生成的 UIWindowsProbe.exe，使用 `-batchmode -logFile <输出>/player.log`。需可用图形设备，Player 不加 -nographics。程序自动退出，在其父目录输出 ui-windows-native-result.json 和 ui-windows.png。

本轮复用 `Build/UIIntegration-20261117/Consumer`，输出 `ui-windows-tests.log`、`ui-windows-build.log`、`ui-windows-player.log`，Player 位于同级 WindowsPlayer；沿用旧目录名不代表运行日期。Build 不入库，长期证据在本目录。验证构建脚本仅用于可丢弃的消费工程。

## 范围

真实射线和合成 PointerClick 覆盖遮罩的输入路径；未声称完整模拟鼠标、触屏、手柄或所有 Input System 设备。仍需主工程 Play 验收、UI 的 IL2CPP / Android / iOS，以及 Profiler 性能测量。本轮不以多 Canvas 的设计代替性能收益证据。DOTween、缓存、复杂路由和动态 Canvas 登记未纳入实现。
