# UI 所有权与失败清理验证（2026-11-17）

团结 1.8.2 / 2022.3.62t4，全量 EditMode **301/301**，0 失败、0 跳过；UI **22/22**，比初始 8 项增加 14 项。报告中实际 test-case 节点数为 301。

- `editmode.xml`：原始 NUnit 报告，按原始字节归档。
- `fingerprints.json`：原始报告 SHA256 与本轮 UI 源码、程序集声明的 UTF-8/LF 指纹。
- 编译/测试日志保存在本机 `Build/UIFix-20261117/editmode.log`，未归档含本机授权信息的完整编辑器日志。UI 编译无 C# 错误。

复现：编辑器关闭时执行以下命令；编辑器已打开时使用 NowFrameWork → Tests → 运行全部 EditMode 测试，避免抢工程锁。

```powershell
& 'D:/UnityEditor/TuanJie/Editor/Tuanjie.exe' -batchmode -nographics -projectPath 'D:/UnityProject/TMP/NowFrameWork' -runTests -testPlatform EditMode -testResults 'D:/UnityProject/TMP/NowFrameWork/Build/UIFix-20261117/editmode.xml' -logFile 'D:/UnityProject/TMP/NowFrameWork/Build/UIFix-20261117/editmode.log'
```

新增覆盖：跨服务句柄、释放超时与异常重试、销毁异常不提前归还租约、Unity 伪空后的释放重试、重复关闭共享清理、首个/第二个等待者取消隔离、关闭中重开 Busy、独立会话可见计数、显示期间取消、打开与回滚同时失败保留原始异常、Shutdown 等待晚到实例/逐项清理/失败重试，以及显隐回调退出前不销毁。

测试使用真实 Unity GameObject / DestroyImmediate 和可控工厂/过渡替身，不依赖帧率。尚未实现真实 UnityUiViewFactory、操作代数、动画反向或 UiModule；本轮没有重建 Player，不能作为真实 Addressables 租约归还、UGUI 渲染/输入或 Android/iOS 的验收证据。
