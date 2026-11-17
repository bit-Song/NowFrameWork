# UI-1 真实工厂与模块接入验证（2026-11-17）

- 团结 1.8.2 / 2022.3.62t4，独立普通消费工程全量 EditMode **330/330**，0 失败、0 跳过；UI **35/35**。报告按原始字节保存于 editmode.xml。
- Windows x64 Development **Mono** Player，使用真实 Addressables 本地 Catalog/Bundle 与 UnityUiViewFactory，完成 **100 次 Open/Close**；日志确认 SessionCount=0、ActiveLeases=0。
- 验证真实延迟 Destroy 的超时保活及稍后重试、正常 Boot 退出和缺少 EventSystem 导致 UI 初始化失败时的回滚；native-result.json 的 passed=true。
- Direct3D 11 离屏 Camera 模式渲染已验证，检测到 2420 个明亮文字像素；ui-native.png 是实际渲染结果，已检查背景、面板和文字。
- 隐藏批处理窗口的背缓冲截图曾为黑色，因此改用明确的 Camera → RenderTexture → ReadPixels 验证。该结果不作为 Overlay 模式交互、键鼠焦点或所有适配策略通过的证明。

## 复现资料

- consumer-manifest.json：独立消费工程的包依赖；框架使用与主工程相同的 file 包源码，外部包来自本机已安装 PackageCache。路径随环境调整。
- UiNativeProbe.cs.txt：运行时探针源文件副本。创建真实 BootSession、调用 Game.UiFlowSample、执行 100 次、销毁超时/重试和失败启动验证，并生成结果。重现时恢复 .cs 扩展名放入独立消费工程的运行时程序集。
- UiValidationBuild.cs.txt：编辑器构建入口副本，创建独立测试资源与场景，构建 Addressables 和 Mono Player。它会重建其独立工程中的测试场景，不应直接拿来覆盖实际项目场景。
- fingerprints.json：原始报告/结果/截图、运行时实际 UI 程序集和当前源码/样例的 SHA256。
- 本机完整日志位于 Build/UIIntegration-20261117/editmode-shipping.log、build-shipping.log、player-shipping.log。含本机路径与授权上下文的完整编辑器日志未归档。

主工程已打开，因此没有另起实例抢主工程锁，也没有关闭用户编辑器。验证工程位于 Build/UIIntegration-20261117/Consumer，Player 位于同级 Player/UIProbe.exe；运行时探针不进入正式框架包或主工程运行程序集。

## 边界

本轮没有重新构建 IL2CPP、Android/iOS，也未自动点击主工程 UI 按钮。UI-1 基础所有权、过渡与最小模块接入完成；模态/返回/导航、Owner、完整焦点恢复、DOTween、UiContext 动态资源登记、有界缓存仍待后续。用户原有 Assets/Scenes/T1.scene 改动保留。
