# 30 · UI 面板动画与 DOTween 接入

> 日期：2026-11-18。已实现模块默认过渡与面板独立覆盖，可使用共享 ScriptableObject 预设配置淡入淡出、位移、缩放及组合。DOTween 放在可选适配包，基础 UGUI 包保持独立。

## 1 运行不同面板动画

打开 [UITween.scene](../../Scenes/UITween.scene)，Addressables Play Mode Script 选择 **Use Asset Database (fastest)** 后 Play。也可通过 **NowFrameWork → Tests → UI → 创建面板动画测试场景** 创建；已有文件不会被覆盖。

| 面板 | Prefab | 预设 | 效果 |
|---|---|---|---|
| Home | Assets/Tests/UITween/Home.prefab | Fade.asset | 淡入淡出 |
| Inventory | Assets/Tests/UITween/Inventory.prefab | Slide.asset | 滑入滑出并渐变透明度 |
| Settings / Confirm | 各自的同名 Prefab | 共用 Pop.asset | 缩放弹出并渐变透明度 |

按钮、返回、模态与焦点沿用 [29](29-UI多窗口导航与Canvas规范.md) 的规则。修改各 Prefab 根节点的 Preset，或编辑预设中的 Enter/Exit 参数，即可改变效果，不用修改加载或导航代码。只想修改一个面板时，复制预设后只赋给该面板。

Settings 与 Confirm 共用预设数据，但各自保存播放状态、Tween 和稳定姿态，不会操作对方的节点。原 UIWindows 场景仍使用模块默认 CanvasGroup 过渡。

本轮动画只控制面板，模态共享遮罩仍按文档 29 的固定颜色策略切换。

## 2 选择优先级

```text
UiModule / UiService 的默认 IUiTransition
    ↓ 面板根存在启用的 UiTransitionBehaviour 时覆盖
该面板自己的过渡组件
```

选择发生在实例 OnOpenAsync 完成后，一次打开固定使用同一个过渡实现，后续 Show、Hide、Close 继续使用它。没有覆盖组件或组件禁用时回退到模块默认，不代表直接跳过动画；需要面板立即切换时可使用零时长预设，或自定义立即过渡组件。

基础扩展点 UiTransitionBehaviour 位于 UGUI 包，不引用 DOTween。特殊动画继承它，实现 ShowAsync / HideAsync 即可；根节点只允许一个过渡组件。实现必须遵守取消后停止写入、等待退出，以及只控制自己拥有的视觉属性的约定。

## 3 在 Inspector 配置

使用 **Assets → Create → NowFrameWork → UI → Tween Preset** 创建资源，在面板根添加 DOTweenUiTransition。

| 字段 | 含义 |
|---|---|
| Preset | 共享的 UiTweenPreset，只存效果参数，不存场景节点 |
| Motion Root | 本面板内部的 RectTransform；位移或缩放必须填写，不能直接使用 UiView 根 |
| Fade Target | 可选 CanvasGroup，留空使用面板根；必须属于本面板 |
| Enter / Exit · Channels | Fade、Move、Scale，可组合；组合通道同时播放，使用同一阶段时间和缓动 |
| Duration / Delay | 当前阶段持续时间与开始延迟，单位秒，必须有限且非负 |
| Ease | Linear、OutCubic、OutBack 等 DOTween 缓动；拒绝 Unset / INTERNAL_* |
| Use Custom Curve / Curve | 使用归一化曲线替代 Ease，端点要求 (0,0) 和 (1,1)，中间可回弹 |
| Alpha | 相对稳定透明度的倍率，范围 0–1；Enter 是隐藏起点，Exit 是隐藏终点 |
| Offset | 相对稳定 anchoredPosition 的位移，使用 Canvas 的 UI 坐标单位；Enter 为起点偏移，Exit 为终点偏移 |
| Scale | 相对稳定 localScale 的逐轴倍率，例如 (0.85,0.85,1) |

例如入场设置 Fade + Scale、Duration=0.24、Ease=OutBack、Alpha=0、Scale=(0.85,0.85,1)，退场设置 Duration=0.16、Ease=InCubic，就能得到独立配置的弹出效果。

稳定姿态在该实例首次播放时、业务 OnOpenAsync 完成后捕获。被隐藏的实例再次显示会使用 Enter 起点；动画中途反向从当前姿态继续。Enter 不启用但 Exit 会修改的通道，在下一次 Show 开始时直接恢复稳定值。

运行中修改共享资产不会影响当前会话。先 Close 再 Open 会重新捕获，即使复用了缓存实例。缓存接入见 [31](31-UI面板缓存与接入.md)。不要在播放过程中改写同一 MotionRoot 的位置/缩放，或让 LayoutGroup/Animator 同时驱动它。

## 4 取消与资源所有权

适配器只保存并终止自己创建的 Tween，取消使用 Kill(false)，保留当前视觉值供反向接管。不使用 KillAll，不按整个节点批量终止其他业务动画；不改变项目的 DOTween 全局时间缩放、Safe Mode 或容量设置。

动画使用非缩放时间，因此 Time.timeScale=0 时仍可播放。若项目主动把 DOTween 自身的全局时间缩放设为 0，本适配不会越过该全局设置。

调用者取消 Show/Hide/导航的等待，仍遵守 UI 服务原有规则；操作反向或服务关闭才取消其持有的工作。打开阶段取消会回滚。普通 Close 会播放退场。服务停止时取消在途退场并跳过尚未开始的退场，因此 DOTween 全局暂停时也可停止服务。销毁和归还租约前，服务会等待在途过渡停止写入。外部 Kill 会使过渡明确失败，不会把中断当成正常完成；失败收尾继续使用 UI 服务的重试机制。

## 5 依赖与接入

新增 `com.now.nowframework.ui.dotween` / `NowFrameWork.UI.DOTween`。宿主自行安装官方 DOTween.dll，本项目已有免费版 1.3.045；程序集使用 overrideReferences 显式引用核心 DLL。

本适配使用通用 DOTween.To，不调用 UGUI 快捷扩展，所以不依赖 DOTween.Modules，也不要求 UNITASK_DOTWEEN_SUPPORT。项目自定义实现如果使用 DOFade、DOAnchorPos 等模块扩展，仍需自行添加对应程序集引用。

基础 UGUI 包只新增过渡组件扩展点，没有反向引用适配包。仅接入基础 UI 的项目无需安装本包和 DLL；当前工程为了展示示例显式安装了本包，编辑器场景生成器也显式引用了它。

参考：[DOTween 官方 API](https://dotween.demigiant.com/documentation.php)。

## 6 验证与后续

- 不安装 DOTween / 适配包的独立消费工程：364/364 EditMode 通过，包含 3 项面板覆盖与默认回退用例。
- 安装可选适配后：377/377 通过，其中基础 UI 57 项、DOTween 适配 13 项。
- Windows Mono Player：真实 Addressables 的三种面板效果、共享预设隔离、Time.timeScale=0、反向无重置、其他 Tween 保留、10 次完整流程与动画中停止均通过；停止后租约与活动 Tween 为 0。

报告、脚本和画面见 [动画证据归档](../evidence/2026-11-18-ui-tween/README.md)。尚未验证 UI 动画的 IL2CPP / Android / iOS 和性能预算。序列时间轴、每通道独立时长、运行中切换预设等高级编排可通过自定义过渡扩展。
