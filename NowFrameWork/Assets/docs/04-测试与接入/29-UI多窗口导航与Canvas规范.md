# 29 · UI 多窗口导航与 Canvas 规范

> 日期：2026-11-18。已实现：独立页面历史、模态顺序、共享全屏遮罩、输入阻挡、返回与基础焦点恢复。当前继续使用立即切换 / CanvasGroup 淡入淡出；DOTween、面板缓存、复杂路由与标签页管理仍待后续。

后续更新：面板独立过渡与 DOTween 可选适配已实现，见 [30](30-UI面板动画与DOTween接入.md)。本页保留多窗口阶段的使用与验证基线。

## 1 在 Unity 中测试

打开 `Assets/Scenes/UIWindows.scene`，或使用菜单 **NowFrameWork → Tests → UI → 创建多窗口导航测试场景** 创建。菜单不会覆盖已有场景或 Prefab。

1. Addressables 的 Play Mode Script 选择 **Use Asset Database (fastest)**，点击 Play。
2. Home 上点击 Next page，进入 Inventory；Back 返回同一个 Home 实例。
3. 点击 Settings，再点击 Confirm，叠加两层模态窗口。下层仍可见，但不接收 UI 输入。
4. Confirm 不允许点击遮罩关闭，使用 Back 或 Esc 关闭它；Settings 恢复输入与有效焦点。
5. Settings 允许点击遮罩关闭；关闭后恢复 Inventory 或 Home。
6. 返回根页后继续 Back 不关闭根页，接口返回 false，项目可以据此显示退出确认。
7. “10 cycles”先返回根页，再重复页面/双弹窗/三级返回；结束保留根页一份会话和租约。点击 Boot 停止后应全部归零。

窗口内部按钮属于 UGUI，会遵守遮罩和输入规则。底部 IMGUI 按钮是开发调试入口，可主动调用操作；它们不属于 UGUI 射线系统。切换页面时存在模态窗口会被 API 拒绝。

入口是 `Assets/Game/UiWindowsSample.cs`，视图脚本是 `UiWindowSampleView`。退出继续遵守 [28 GameEntry](28-GameEntry服务入口与生命周期.md) 的停止门禁与所有者清理顺序。

## 2 页面、模态与返回

### 页面历史

```csharp
await GameEntry.UI.Navigation.PushAsync(homeKey, homeArgs, ct);
await GameEntry.UI.Navigation.PushAsync(inventoryKey, inventoryArgs, ct);
await GameEntry.UI.Navigation.PopAsync(ct);
```

Push 创建新页面，进入成功后隐藏前页。Pop 在当前页下方恢复前页，当前页留在前景完成退场，两页在过渡期间均不接收 UI 输入。历史页保持实例、参数与租约，不重复调用 Open。根页不会被 Pop 移除。历史中已有的页面需通过 Pop 返回；再次 Push 当前页返回 AlreadyOpen。导航不接管通过普通 Open 打开的已有页面。

Push/Pop 同一时刻只允许一个操作，其他 Push/Pop 明确报忙；Back 在过渡期间被消费，不排队连退。调用者 ct 只取消自己的等待，导航仍持有工作；服务 Shutdown 取消内部导航，并等待它退出后清理所有会话。

页面显示失败保持原历史；Push 后续失败会关闭新页并恢复旧页。回滚清理失败保留句柄，下次导航先重试。Pop 关闭失败时保留历史；下次 Pop 会完成原关闭，Push 也会先完成原关闭再打开新页。业务不得同时对导航拥有的页面直接 Show/Hide/Close。

### 模态窗口

```csharp
new UiDefinition(3, "Settings", settingsPrefab)
{
    Layer = UiLayer.Popup,
    Modal = true,
    CloseOnBackdrop = true,
    BackBehavior = UiBackBehavior.Close
};
```

普通 Popup 使用 OpenAsync / CloseAsync；模态也是 Popup 的一种策略，不进入页面历史。Modal 只能属于 Popup 层，不能设置返回 Ignore；CloseOnBackdrop 仅适用于模态窗口。服务在构造时复制注册定义，后续修改原定义不影响运行中的注册表。

UiModule 创建一张共享遮罩，直接挂在 UiRoot 下，覆盖完整视口。它位于最高可见模态窗口下方，新弹窗不会新增一层黑色图片。显示开始前调整遮罩顺序，退场期间保持阻挡，隐藏完成后才移到下一模态下方或停用。当前遮罩采用 Root 配置的固定颜色，不单独播放淡入淡出。

被模态覆盖的窗口禁用所属 GraphicRaycaster 和 CanvasGroup 交互；Toast 不接收输入，也不参与返回。覆盖不会触发 Hide，不取消被覆盖窗口的 VisibleToken，也不会暂停游戏世界或业务 InputActionMap。此输入规则只管理本 UI 服务，游戏输入仍需由项目输入层决定。

### 返回与焦点

```csharp
bool handled = await GameEntry.UI.RequestBackAsync(ct);
// 也可以直接使用 GameEntry.UI.Navigation.BackAsync(ct)。
```

存在导航历史时优先处理 Popup，再返回上一页；没有导航历史时处理普通窗口的视觉顺序。Toast 跳过。BackBehavior 为 Close 时默认关闭，Block 消费但不关闭，Ignore 允许非模态窗口向下透传。视图可覆盖同步 `OnBackRequested()` 并返回 true，自行处理返回；需要异步确认时由业务 TaskRunner 发起，不等待一个必须依赖自身回调退出的操作。

开始显示/隐藏或关闭的窗口消费返回，不继续关闭下层。Esc/设备返回输入由项目适配并调用接口，基础 UI 包不依赖 Input System。

窗口被阻挡前记录自身有效选中控件，恢复后先尝试该控件，否则尝试 UiView 的 Default Focus；目标必须仍存在、激活且可交互。它不会重建已销毁的控件，也不扫描并猜测任意默认按钮。

## 3 Prefab 与排序

```text
UiRoot                         Canvas + CanvasScaler
├─ Page / Popup / Toast         三个全屏容器，不必额外添加 Canvas
├─ Shared Modal Backdrop        运行时创建；全屏、位于顶层模态下
└─ Navigation Input Blocker     过渡时启用的透明 UI 挡板

Window                         UiView 派生脚本 + Canvas + CanvasGroup
├─ FullscreenBackground        可选，全屏背景
└─ SafeAreaContent              可选 UiSafeArea
   └─ MotionRoot               建议用作位移/缩放动画目标
      ├─ Background / Title / Controls
      └─ DynamicRegion         只有需要隔离高频刷新时才增加子 Canvas
```

名字仅供示意，框架不按子节点名称寻找业务控件。根 RectTransform 应铺满所属容器，弹窗实际可见区域放在内部。控件引用由脚本序列化绑定，示例由编辑器生成这些引用。

新建 Root 的三层位于全屏区域，内容通过 UiSafeArea 适配；既有 Root 中位于 SafeArea 下的层继续兼容，不会被自动重挂。旧层已经适配安全区时，不要再次给同一内容重复添加 UiSafeArea。共享遮罩始终独立于旧 SafeArea 层。

服务负责根 Canvas 的 sortingLayer / sortingOrder；内部子 Canvas 默认继承排序即可。若确实开启 overrideSorting，子 Canvas 在 Prefab 中相对根的原始 sortingOrder 只能为 0–30，服务统一搬移并保留这个相对偏移。禁止业务随意设置全局高排序绕过模态规则。

当前每层最多同时持有 128 个会话（含隐藏和清理失败），每个可见窗口保留 32 个排序值，层之间保留 8192。Root sortingOrder 允许 -32768–8191，为三层窗口和导航挡板留足范围。超出配置或容量时明确报错，不截断导致窗口串层。

视图创建时采集 Canvas、GraphicRaycaster、Animator 及原启用状态。需要输入的子 Canvas 配置自己的 GraphicRaycaster，装饰区域关闭 Raycast Target；子组件原本禁用时，Show 不强行打开它。子 CanvasGroup 不允许开启 ignoreParentGroups 绕过窗口输入范围，打开时会检查并回滚无效配置。创建后动态增加独立 Canvas/Raycaster 的登记接口仍未提供，不要用它绕过已采集的显隐和输入管理。

## 4 动静分离的取舍

每个面板保留根 Canvas，先保证面板生命周期独立，再按更新频率拆子 Canvas。静态背景、每秒变化的计时器、连续变化的血条，不一定需要同一个刷新边界；但不默认给每个控件增加 Canvas，也不把所有面板的静态内容搬到一个全局 StaticCanvas。

拆分可缩小批次重建影响范围，但 Canvas 之间不能合并为同一个批次，可能增加绘制调用。子 Canvas 也不保证隔离跨父子 LayoutGroup/ContentSizeFitter 的布局传播；MotionRoot 避免与布局系统争写同一个 RectTransform。先使用 Profiler / Frame Debugger 比较 CPU、批次数与 GPU 耗时，再决定是否继续细分。本轮不宣称已有性能收益数据。

参考依据：[Unity UI 优化说明](https://unity.com/how-to/unity-ui-optimization-tips)、[UnityScreenNavigator 页面与模态设计](https://github.com/Haruma-K/UnityScreenNavigator#basic-screen-transition)、[共享遮罩策略](https://github.com/Haruma-K/UnityScreenNavigator#changing-the-behavior-of-the-modal-backdrop)、[StarForce 面板 Canvas 深度管理](https://github.com/EllanJiang/StarForce/blob/master/Assets/GameMain/Scripts/UI/UGuiForm.cs)。这里借鉴设计，未安装或替换为上述第三方 UI 框架。

## 5 验证与后续

EditMode 全量 361/361 通过，新增 18 项覆盖窗口排序、模态、焦点、导航与回滚等边界。Player 验证结果见本轮证据归档；原始测试与验证脚本保存在 `docs/evidence/2026-11-18-ui-windows/`。

后续仍需独立推进：DOTween 适配、有界缓存、Replace/Reset/深链接等路由、标签页容器、动态 Canvas 登记、更复杂的焦点策略，以及移动端/IL2CPP 和性能验证。

## 6 键盘与手柄焦点导航

普通菜单和弹窗采用 Unity UGUI 原生导航。`UIWindows.scene` 样例已接入，可以使用方向键、手柄十字键或摇杆在按钮间移动，并用确认键点击当前按钮。

### 原生导航接入点

- 项目在传给 `UiRoot` 的 `EventSystem` 上配置 `InputSystemUIInputModule`。方向键、手柄十字键和左摇杆绑定到 `Move`，确认键绑定到 `Submit`。返回键由项目输入层统一调用 `IUiService.RequestBackAsync()`，同一次输入只分发一次。 [Unity Input System UI 模块](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.14/manual/UISupport.html)
- 面板用 `UiView.DefaultFocus` 指定初始选中控件。简单布局用 `Selectable.Navigation` 的 `Automatic`，需要固定方向关系的弹窗和网格用 `Explicit`，目标限定在当前可交互面板内。开关模态继续使用窗口层已有的焦点清理与恢复。 [Unity UGUI 导航选项](https://docs.unity3d.com/2022.3/Documentation/Manual/script-SelectableNavigation.html)
- 一列或一行控件可调用 `ConfigureVerticalNavigation(buttons)` 或 `ConfigureHorizontalNavigation(buttons)`。传入当前面板的完整控件顺序。框架用 Unity 的显式链接连接可用项，并将首项设为默认焦点。面板打开时或按钮显隐、禁用、排序改变之后重新配置。网格可直接配置 Unity 的四向显式链接。

### 虚拟列表参考方案

背包等虚拟列表可参考 CanvasCore 的 `RecycledScrollNavigator`。由列表保存选中项的**数据索引**。方向输入先计算下一个有效索引，必要时用 `ScrollTo(index)` 将目标滚入视口。等复用单元格绑定到该条数据后，再通过 `EventSystem` 选中它的 `Selectable`。交接焦点前确认列表仍属于当前可交互面板，且单元格仍绑定目标索引。CanvasCore 用作实现参考。 [CanvasCore 虚拟列表导航与背包示例](https://github.com/aexxacsw/CanvasCore#gamepad-and-keyboard)

接入时验收键盘与手柄的移动、确认和返回。模态打开时焦点留在弹窗内，关闭后恢复仍有效的选中项。虚拟列表滚出视窗或回收单元格时，焦点跟随数据项，确认操作作用于当前选中数据。
