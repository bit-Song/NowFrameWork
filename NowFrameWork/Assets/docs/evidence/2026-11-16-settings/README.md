# 设置存储验证证据（2026-11-16）

- editmode.xml：团结 1.8.2 全量 EditMode，256/256 通过；存储新增 50 项，其中 Core 38、Unity JSON 12。
- standalone.txt：.NET 9.0.315 Core 源文件独立编译/文件烟测结果；不引用 Unity、UniTask 或 HybridCLR。为激活线程约束，探针显式定义 DEVELOPMENT_BUILD。
- Program.cs.txt / Probe.csproj.txt：复核用探针源文件；复制到独立构建目录并去掉 .txt 后缀运行。csproj 中 Core 源文件路径是原机绝对路径，异机须调整。执行 dotnet run --project Probe.csproj -c Release；此配置仍显式定义 DEVELOPMENT_BUILD，不作为正式构建调用擦除证据。
- manifest.json：原始报告及当次运行时代码 SHA256，便于比对后续修改；XML 按原始字节，源码及探针文本按 UTF-8/LF 归一化后计算。测试原始日志位于忽略的 Build/Settings-20261116/review.log，未归档整个编辑器日志。

未重建 Windows Player，未进行 Android/iOS、硬件断电或真实进程终止实验。中断用例构造对应的主/备份/临时文件状态，并用真实文件系统触发写入和备份轮换失败。
