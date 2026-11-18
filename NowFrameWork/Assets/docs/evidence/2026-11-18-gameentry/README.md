# GameEntry 接入验证

日期：2026-11-18。团结 1.8.2 / 2022.3.62t4。基于远端 `28d2b8c` 加入 GameEntry、项目入口改造与新增测试；源码指纹见 fingerprints.json。

## 结果

- [EditMode 原始报告](editmode.xml)：343/343，0 失败、0 跳过。已有模块/Boot 279 + UI 36 + 销毁工具 3 + 资源键生成器 13 + GameEntry 12。比上一轮 330 增加远端 UI 工厂修复 1 项和本次 GameEntry 12 项。
- [Windows Player 原始结果](native-result.json)：passed=true。Development Mono x64、真实 Addressables 本地内容，实际 UiFlowSample 验证 GameEntry 和 Boot/UI 使用相同实例，100 次打开关闭后计数归零，停止后无法获取服务，重新启动绑定新服务，第二次停止解绑。
- Player 同时回归销毁超时保留租约、重试释放、UI 启动失败回滚与相机离屏渲染。资源示例 AssetLoadDemo 的加载失败解绑由 EditMode 覆盖，其成功实例化路径未在本轮 Player 单独运行。

## 复现

1. 使用独立消费工程，包引用仓库 Framework 源码；依赖快照见 [consumer-manifest.json](consumer-manifest.json)。复制主工程 Boot/Boot.Addressables（不复制 HybridCLR）、Game、Generated、资源键生成器及其测试、Assets/Tests/GameEntry，以及原 UI 验证夹具中的 Addressables 配置和最小资产。
2. 以团结编辑器执行 `-batchmode -nographics -projectPath <消费工程> -runTests -testPlatform EditMode -testResults <输出>/editmode.xml -logFile <输出>/editmode.log`。不对已打开的主工程启动第二实例。
3. 采用上一轮 [UI 验证夹具](../2026-11-17-ui-integration/README.md) 的程序集与资产组织，将本目录的 [UiNativeProbe.cs.txt](UiNativeProbe.cs.txt) 和 [UiValidationBuild.cs.txt](UiValidationBuild.cs.txt) 放入独立消费工程对应运行时/Editor 程序集，移除 .txt 后缀。Editor 脚本仅用于可丢弃的消费工程，会重建其中的 UI 验证场景。
4. 执行 `-batchmode -nographics -quit -projectPath <消费工程> -executeMethod UiValidationBuild.PrepareAndBuild -logFile <输出>/build.log`，构建真实 Addressables 内容与 Windows Mono Player。
5. 运行生成的 UIProbe.exe，参数 `-batchmode -logFile <输出>/player.log`。程序自动退出，在其父目录生成 gameentry-native-result.json。图形验证需可用图形设备，不能给 Player 加 -nographics。

本轮工作目录为 `Build/UIIntegration-20261117/Consumer`，输出日志为同级 `gameentry-tests.log`、`gameentry-build.log` 和 `gameentry-player.log`。目录名沿用已有夹具，运行日期为 2026-11-18；Build 不入库，长期报告保存在本目录。

## 范围

GameEntry 仅为项目业务入口，框架包不依赖它。测试不代表任意调用者能够跳过初始化顺序，也不代表缓存引用会在停止时自动失效；取消、实例和资源租约继续由所有者管理。本轮未验证 UI 的 IL2CPP、Android/iOS 或主工程场景交互。
