# 24 · Addressables 资源键生成器

> 2026-11-17。编辑器菜单：**NowFrameWork → Assets → 资源键生成器**。只读取已登记且参与构建的 Addressables 资源，不自动添加资源，不修改 Address。

## 使用顺序

1. 打开生成器，填写资源根目录。当前工程可先用 `Assets/Tests/PCRegression`；实际项目可使用自己建立的 `Assets/GameRes`。根目录必须存在。
2. 点击 **1. 扫描资源与冲突（不写文件）**。
3. 检查列表中的代码引用、原始路径、Group 和 Address；可以通过“定位资源”在 Project 中定位。
4. 无问题时点击 **2. 预览后确认生成…**，再在确认弹窗中选择“确认生成”或“取消”。有冲突时生成按钮不可用，修正后重新扫描。

扫描会跳过没有打包 Schema 的内置 Group，以及未勾选 Include In Build 的 Group。登记为文件夹的 Addressables 条目会展开其可寻址子资源，不逐项生成 Sprite/FBX 等文件内部子对象。

输出固定为 `Assets/Generated/AssetKeys/AssetKeys.g.cs`，同时建立专用程序集 `NowFrameWork.Generated.AssetKeys`。这两个文件只有在确认后才创建。业务 asmdef 需要显式引用 `NowFrameWork.Generated.AssetKeys`，调用加载 API 时还须引用 `NowFrameWork.Assets`、`UniTask` 等实际使用的程序集。

## 生成规则

根目录 `Assets/GameRes` 下：

| 原始资源 | 代码引用 |
|---|---|
| `Battle/Enemy/Goblin.prefab` | `AssetKeys.Battle.Enemy.Goblin_prefab` |
| `Collection/Preview/Goblin.prefab` | `AssetKeys.Collection.Preview.Goblin_prefab` |
| `UI/Icon.png` | `AssetKeys.UI.Icon_png` |
| `UI/Icon.prefab` | `AssetKeys.UI.Icon_prefab` |

只去掉指定根目录，保留文件夹层级；末级使用“去掉最后一个扩展名的文件名 + 下划线 + 扩展名”，大小写保留。例如 `HotUpdate.dll.bytes` 生成 `HotUpdate_dll_bytes`。

生成字段的值使用资源当前 Address，目录层级与 Address 独立：`Battle/Enemy/Goblin.prefab` 的 Address 可以仍为 `Enemy/Goblin`。Group/Bundle 名不加入代码层级，调整分组不会因为 Group 名变化而重命名字段。

```csharp
using NowFrameWork.Generated;

var lease = await assets.AcquireAsync<UnityEngine.GameObject>(
    AssetKeys.Battle.Enemy.Goblin_prefab, ct);
// 按实例生命周期持有并归还 lease。
```

当前工程选择 `Assets/Tests/PCRegression` 时，对应引用为 `AssetKeys.Probe_prefab`、`AssetKeys.Config_txt` 等。

## 冲突处理与保护

- 文件夹和文件名中的非字母、数字、下划线字符转为 `_`；数字开头和 C# 关键字加 `_` 前缀。中文字符可保留为合法标识符。
- 转换后不同文件/文件夹得到同名引用、字段与文件夹同名、嵌套类型与父类型同名时，显示冲突双方路径和 Group；不静默覆盖或合并。
- 检查重复 Address，包括根目录内的资源与其它参与构建 Group 中资源的地址冲突。
- 空 Address、缺少扩展名、空扫描结果、非法命名空间和未启用 Include Addresses in Catalog 会阻止生成。
- 根目录为 `Assets` 时，无法解析 GUID 的失效 Addressables 条目会报告其 Group、Address 和 GUID。选择更小的根目录时，无法定位路径的条目不会纳入该目录的生成范围；这不代表整个工程的失效资源已被修复。
- 确认前重新扫描；扫描后资源有变化会刷新预览，要求重新检查。确认弹窗关闭后也会复查，防止写入旧快照。
- 已生成代码在扫描失败、冲突或取消时保持不变。非本工具生成的同路径代码、不同内容的同路径 asmdef 会拒绝覆盖。写入单个文件采用临时文件替换。
- 重新扫描会提示已生成代码是否与当前资源一致；本版本没有自动构建阻断钩子，需要在资源调整后主动扫描/生成。

移动目录、重命名资源或改变根目录，会改变代码引用；重新生成后通过编译错误查找旧引用。已有 Address 不会自动修改。Address 改名后重新生成，代码引用按文件路径保持不变，字段值更新为新 Address。

## 验证范围

纯生成逻辑新增 13 项 NUnit 用例，使用团结附带的 Mono 执行，13/13 通过；编辑器源码通过工程现有 Unity/Addressables 引用编译，生成示例也已针对实际 Assets 程序集编译通过。本机输出位于 `Build/AssetKeys-20261117/`。

现有 Unity 窗口恢复自动化时 `activate_window` 两次超时，未启动抢占工程锁的第二个实例。因此本轮尚未执行窗口点击验证及 Unity 全量 EditMode 回归；新增用例会出现在 `NowFrameWork.AssetKeys.Editor.Tests` 中，可用现有全量测试菜单运行。
