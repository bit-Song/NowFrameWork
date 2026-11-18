# UI 面板动画验证

日期：2026-11-18。团结 1.8.2 / 2022.3.62t4，DOTween 免费版 1.3.045。基于同次工作区中的 UI 多窗口阶段，新增面板过渡覆盖和可选 DOTween 包；源码及 DLL 指纹见 fingerprints.json。

## 结果

- [无 DOTween 的基础包报告](base-without-dotween.xml)：364/364，0 失败、0 跳过。该轮依赖见 [base-consumer-manifest.json](base-consumer-manifest.json)，运行时没有复制 DOTween 插件，验证基础 UI 独立编译与运行测试。
- [带适配包的完整报告](editmode.xml)：377/377，0 失败、0 跳过。已有模块/Boot 279 + 基础 UI 57 + DOTween 13 + 销毁工具 3 + 资源键 13 + GameEntry 12。
- [Windows Player 报告](native-result.json)：passed=true。Development Mono x64，真实 Addressables 加载 4 份面板 Prefab、3 份预设；验证 Fade/Slide/Pop、生效中的位移与缩放、反向前后当前姿态一致、共享预设独立状态、Time.timeScale=0、保留其他 Tween、10 次完整流程，以及动画中停止。最终 ActiveLeases=0、活动 Tween=0。
- [离屏渲染截图](ui-tween.png)：实际弹窗布局与文字已人工检查。运动中的属性与反向由 Player 断言覆盖，静态截图不作为动画流畅度或性能证据。

## 复现

沿用 [UI 多窗口夹具](../2026-11-18-ui-windows/README.md)。本轮工作目录 `Build/UIIntegration-20261117/Consumer`，包直接引用仓库 Framework 源码；项目 Game、编辑器菜单、场景和资产与交付副本一致。

1. 基础独立测试使用 base-consumer-manifest.json，既不安装新适配包，也不复制插件。只保留基础 Game/Boot 和此前窗口夹具；不要加入本轮 UiTweenSceneMenu、动画验证脚本或对适配程序集的引用。执行 -runTests -testPlatform EditMode，包含 UiTransitionSelectionTests。该轮是在添加动画示例之前运行，不能仅替换已接入动画示例工程的 manifest 来复现。
2. 适配测试使用 [consumer-manifest.json](consumer-manifest.json)，增加 ui.dotween 包与 testables，复制宿主官方 DOTween.dll 和 meta。本验证仅复制核心 DLL，没有复制 DOTween.Modules 或启用 UniTask DOTween 条件扩展。
3. 使用 -runTests -testPlatform EditMode 生成完整报告。UiTweenTests 采用 ManualUpdate，手动驱动确定性时间，不依赖帧率。
4. DOTween.Init 在非 Play 模式不会设置 initialized，而实例 Kill 以该标志为前提。测试夹具仅在固定版本 EditMode 手动宿主中通过反射暂时设置该标志，结束后恢复；不创建全局驱动对象、不改运行时实现，也不调用全局 KillAll。这一前提另由真实 Player 的正常初始化验证。
5. 将 [UiTweenNativeProbe.cs.txt](UiTweenNativeProbe.cs.txt) 放入 UIValidation.Runtime、[UiTweenValidationBuild.cs.txt](UiTweenValidationBuild.cs.txt) 放入 UIValidation.Editor，移除 .txt 后缀。两个程序集在原 UI 夹具引用上增加 NowFrameWork.UI.DOTween，允许引用宿主核心 DLL。
6. 执行 `-batchmode -nographics -quit -projectPath <消费工程> -executeMethod UiTweenValidationBuild.PrepareAndBuild -logFile <输出>/build.log`。构建脚本仅用于可丢弃的消费工程，生成 UITween 场景、预设和 Addressables 本地内容。
7. 运行 TweenPlayer/UITweenProbe.exe，参数 `-batchmode -logFile <输出>/player.log`，Player 不加 -nographics。自动输出父目录下的 ui-tween-native-result.json 和 ui-tween.png，然后退出。

本轮完整日志：ui-tween-base.log、ui-tween-shipping-tests.log、ui-tween-build.log、ui-tween-player.log；Build 不入库，本目录保留长期产物。这里的基础 manifest 复用了同配置的前一阶段快照。

## 范围

本轮验证 Windows Mono 与 EditMode。移动设备、IL2CPP、所有输入设备和 Profiler 性能仍需单独验收；未加入面板缓存或复杂时间轴。预设配置错误会明确失败并回滚，不静默替换为默认效果。
