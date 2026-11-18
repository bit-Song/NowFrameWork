# NowFrameWork UI DOTween

可选的 UGUI 面板过渡适配。基础 UGUI 包不引用本包，也不要求安装 DOTween。

在已接入 UGUI 基础包的项目中安装官方 DOTween 核心 DLL，再添加本包的本地 UPM 引用。当前验证版本为 DOTween 免费版 1.3.045、Unity/团结 2022.3 系列；本包不重新分发插件。运行时程序集显式引用 DOTween.dll，通过通用 `DOTween.To` 驱动属性，不依赖 DOTween.Modules 或 UniTask 的 DOTween 条件扩展。

## 配置

1. 使用 **Assets → Create → NowFrameWork → UI → Tween Preset** 创建 UiTweenPreset。
2. 在 UiView 同一 Prefab 根节点添加 DOTweenUiTransition，选择 Preset。
3. 使用位移或缩放时，显式指定本面板内部的 MotionRoot；Fade Target 可留空，默认使用 UiView 的 CanvasGroup。
4. Enter/Exit 分别配置通道组合、时间、延迟和 Ease，或使用从 (0,0) 到 (1,1) 的自定义曲线。

Enter 的 Alpha/Offset/Scale 表示隐藏起点，Exit 表示隐藏终点；Alpha 是稳定透明度的比例，Offset 是相对稳定 anchoredPosition 的偏移，Scale 是相对稳定 localScale 的倍率。通道同时播放，不同通道暂不支持独立时间轴。

## 生命周期

服务在 OnOpenAsync 完成后选定面板覆盖，未配置或禁用组件时使用模块默认。预设数据在首次播放时复制，运行姿态保存在每个组件实例中；修改共享资产不会改变已打开实例。

反向从当前值继续，稳定隐藏后再次显示才使用 Enter 起点。取消结束前停止本次 Tween，使用 Kill(false)，不跳到终点、不调用全局 KillAll。使用非缩放时间，不改变项目的 DOTween 全局设置。场景外部不要直接销毁或改写服务拥有的过渡目标。

特殊效果可在项目中继承基础包的 UiTransitionBehaviour。完整场景、API 与验证范围见项目文档 30。

API 依据：[DOTween 官方文档](https://dotween.demigiant.com/documentation.php)。
