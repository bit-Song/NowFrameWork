# 2026-11-25 · 音频接入编辑器验证

> 场景：`Assets/Scenes/BootIntegration.scene`，探针 `AudioProbe`（自动播放：2s BGM、5s SFX）。

## 结果

| 检查 | 结果 |
|---|---|
| 音频模块状态 | 运行中 |
| BGM 播放 | 正常 |
| SFX 播放 | 正常 |

## 过程中排查的问题

| 现象 | 原因 | 处理 |
|---|---|---|
| `发布目录中找不到唯一资源: audio/sfx` | 编辑器启用了 Windows 发布恢复，加载的是 fixtures 的 v3 Catalog（只含 `ReleaseProbe/Config`） | 把 `WindowsReleases` 移到 `WindowsReleases.bak-20261125`，改用随包 Addressables |
| Android 上 `PlatformNotSupportedException` 阻断 Boot | `WindowsResourceReleases.SelectAsync` 在非 Windows 直接抛异常 | 非 Windows x64 跳过发布选择、使用随包内容；发布探针自动隐藏 |

## 未覆盖

- Android 真机：音频就绪、前后台 `IsPaused` 切换、`Application.lowMemory` 低内存回收；
- 音频与发布恢复同时启用时的资源来源策略（当前发布 Catalog 需包含音频键，否则会优先发布集而找不到）。
