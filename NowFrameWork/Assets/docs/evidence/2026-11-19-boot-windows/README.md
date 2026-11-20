# Boot Windows IL2CPP 异常路径验收

2026-11-19，团结 1.8.2 / 2022.3.62t4，框架基线 `97e1c38`。主工程用 `BootIntegration.scene` 构建 Windows x64 IL2CPP Development Player，采用 Low stripping、普通 AOT、本地 Addressables 和 Direct3D 11。三条路径在同一进程中通过，退出码为 0。 [原始结果](boot-windows-result.json) · [构建与退出记录](run.json)

| 场景 | 通过条件 |
|---|---|
| 必需配置缺失后重试 | 真实 Addressables 查询缺失地址，Boot 报告 `Failed` 和 `FailureStage=Config`，入口未执行。验收脚本将测试地址映射到现有的 `PCProbe/Config` 后，同一 BootSession 使用新上下文重新进入 `Running`。 |
| 启动中停止后重新启动 | 入口在持有一份真实配置租约时等待，调用 `StopAsync()` 取消启动，入口退出一次，随后用新上下文重新启动。 |
| 释放失败后重试停止 | Provider 在调用 Addressables 释放前抛出一次异常。Boot 进入 `CleanupFailed`，保留原上下文和一份租约并拒绝启动。第二次停止归还同一个底层句柄。 |

三条路径完成清理后均为零租约、零待处理资源请求。验收脚本在运行时停用样例自带的自动启动，并直接调用真实 `BootSession`。测试入口控制取消时机，Provider 包装器负责一次性故障注入。设置读写使用本轮独立目录。

## 复现

1. 将 [运行探针](BootWindowsProbe.cs.txt) 和 [构建脚本](BootWindowsAcceptanceEditor.cs.txt) 复制到主工程的临时目录，移除 `.txt` 后缀。运行探针的程序集引用 `NowFrameWork.Boot`、`NowFrameWork.Boot.Addressables`、`NowFrameWork.Assets`、`NowFrameWork.Assets.Addressables`、`NowFrameWork.Core`、`NowFrameWork.Unity` 和 `UniTask`。将构建脚本放在独立的 Editor 程序集中，引用 `Unity.Addressables`、`Unity.Addressables.Editor` 和 `HybridCLR.Editor`。
2. 将 `NWF_BOOT_WINDOWS` 指向已创建的绝对输出目录。对主工程运行团结编辑器命令 `-batchmode -nographics -quit -buildTarget Win64 -executeMethod BootWindowsAcceptanceEditor.Build`。
3. 在保留该环境变量的情况下运行生成的 `BootWindowsAcceptance.exe -batchmode -force-d3d11 -screen-fullscreen 0`。核对输出目录中的 JSON 和进程退出码。
