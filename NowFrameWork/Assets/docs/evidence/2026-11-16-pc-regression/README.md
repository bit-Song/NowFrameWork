# Windows PC 回归证据（2026-11-16）

七次 Player 运行全部通过，共 28 个用例条目（包含重复的 INIT/CLEANUP）；EditMode 185/185。

V1/V2 输出分别为 PC-PROBE-V1、PC-PROBE-V2，HTTP 日志记录不同 DLL bundle 下载，Player 的 EXE、GameAssembly.dll、TuanjiePlayer.dll 指纹一致。

[manifest.json](manifest.json) 保存核对摘要、Player 指纹和归档文件 SHA-256；各子目录保留原始 report.json、run.json、http.jsonl。原始 run.json 中绝对路径指向当时的 Build 目录，报告未改写。

测试构建与协议仅覆盖 Windows 本轮夹具；Android/iOS、H03 元数据本轮重验、兼容清单/回滚、进程中断与缓存损坏不在本次通过范围。
