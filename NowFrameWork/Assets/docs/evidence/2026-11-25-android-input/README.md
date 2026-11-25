# 2026-11-25 · Android 真机验证（输入 / UI / 本地化）

> 由用户在本机真机复测确认；本目录暂未归档 logcat 与截图，后续补。

## 覆盖场景

| 场景 | 验证内容 | 结果 |
|---|---|---|
| `UIWindows.scene` | 系统返回关面板、根页面双按退出、模态遮罩拦截、面板交互 | 通过 |
| `UILocalization.scene` | 中英切换、缺译回退、重启保持 | 通过 |
| `InputCameraDemo.scene` | 触屏摇杆移动（相机相对）、跳跃按钮、双指独立 | 通过 |

## 本轮改动对应的验证点

| 改动 | 真机表现 |
|---|---|
| `InputSystemDeviceWatcher` 订阅 `InputSystem.onEvent` | 设备类型提示正常，Console 无断言刷屏 |
| 改键改轮询捕获 | 键盘/手柄改键可用，无 `GetStatePtrFromStateEvent` 断言 |
| `VirtualJoystick` 多指 `pointerId` | 双指同时操作摇杆与跳跃按钮互不干扰 |
| `CharacterDemo` 运行时生成触屏控件 | 摇杆与按钮位置随屏幕自适应 |

## 未覆盖

- 音频前后台恢复、低内存回收、物理手柄真机；
- logcat 与截图证据未归档。
