# 2026-11-25 · Android 音频真机验证

> 设备：用户自备 Android 真机；包由用户自行构建安装。

## 已确认

| 检查 | 结果 |
|---|---|
| 音频模块就绪 | 通过（用户复测） |
| BGM 播放 | 通过 |
| SFX 播放 | 通过 |

## 待确认

| 检查 | 方法 |
|---|---|
| 切后台自动暂停 / 回前台恢复 | `adb shell input keyevent KEYCODE_HOME` 后回前台，观察 `[AudioProbe] IsPaused` |
| 低内存回收 | `adb shell am send-trim-memory <包名> RUNNING_LOW`，观察 `Application.lowMemory` 计数与 UI 缓存回收 |

## 备注

- 包名：`com.UnityTechnologies.com.unity.template.urpblank`；
- 探针自动播放：启动 2 秒 BGM、5 秒 SFX，便于免点击验证；
- 未归档 logcat 原文，待补。
