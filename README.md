# potplayer-hold-speed

给 Windows 版 PotPlayer 加上 B 站式的 **「长按 → 倍速播放」**：

- **短按 →**：照常快进
- **按住 →**（超过 0.3 秒）：2x 播放
- **松开**：回到按下前的速度（原来是 1.25x 就回到 1.25x）

倍速功能跟着 PotPlayer 一起启动：双击视频文件、桌面或开始菜单快捷方式打开 PotPlayer 时自动开启，PotPlayer 全部关闭后自动退出。不常驻后台，不需要开机自启。

无需安装 AutoHotkey 等第三方软件，只用到 Windows 自带的 .NET Framework 4.x 编译器。

## 安装

1. 确认 PotPlayer 的倍速快捷键是默认的 `C`（加速）/ `X`（减速），倍速调整幅度为默认的 0.1。
2. 下载本仓库，在 PowerShell 中运行：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
   ```

   - PotPlayer 路径从注册表自动读取；读取不到时用 `-PotPlayer "D:\PotPlayer\PotPlayerMini64.exe"` 指定。
   - 开始菜单 / 公共桌面的快捷方式位于所有用户共享的目录，需要用**管理员** PowerShell 运行才能修改；其他部分不需要管理员权限。
   - 要写入约 100 多种文件类型的关联，整个过程可能需要几分钟。

安装后托盘里会出现 PotPlayer 图标（悬停显示 `PotPlayer: hold Right for 2x`），右键可以退出。

## 卸载

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

会恢复原来的快捷方式、删除本工具添加的文件关联，并删除 `%LOCALAPPDATA%\PotPlayerHoldSpeed`。

## 自定义

修改 [`PotPlayerHoldSpeed.cs`](PotPlayerHoldSpeed.cs) 开头的常量，然后重新运行 `install.ps1`：

| 常量 | 默认值 | 含义 |
| --- | --- | --- |
| `TRIGGER_VK` | `0x27`（→） | 触发键的[虚拟键码](https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes) |
| `HOLD_MS` | `300` | 按住超过多少毫秒算「长按」 |
| `STEPS` | `10` | 长按时按几次加速键；10 × 0.1 = +1.0，即 1x → 2x。设为 20 就是 3x |

## 工作原理

- `install.ps1` 用系统自带的 `csc.exe` 把 `PotPlayerHoldSpeed.cs` 编译成启动器 `%LOCALAPPDATA%\PotPlayerHoldSpeed\PotPlayerHoldSpeed.exe`。
- 在 `HKCU\Software\Classes` 下为 PotPlayer 的每个 ProgID 添加按用户生效的覆盖设置，把打开命令改成调用启动器；PotPlayer 自己写在 HKLM 的设置保持不变。指向 PotPlayer 的快捷方式也改成指向启动器（图标不变）。
- 启动器先把参数原样交给 PotPlayer，再用互斥锁保证只运行一个倍速监听。监听使用低级键盘钩子，只在 PotPlayer 位于前台时生效：短按时重新发送一次 →；长按时发送 N 次 `C`，松开时发送 N 次 `X`。

## 限制

- 一直按住 → 原本会连续快进，现在会变成倍速播放；短按快进会晚 0.3 秒才触发。
- 通过「打开方式 → 选择其他应用 → 始终使用」重新指定 PotPlayer，或直接运行 PotPlayer 的 exe 时，不会经过启动器。如果出现这种情况，重新运行 `install.ps1` 即可。
- 如果加速时达到了 PotPlayer 的速度上限，松开后恢复的速度可能不准确。

## English

Hold the Right arrow in PotPlayer (Windows) to play at 2x; release to restore the previous speed, and tap to seek as usual. `install.ps1` compiles a small launcher with the built-in .NET Framework `csc.exe` and routes PotPlayer's shortcuts and file associations through it, using per-user `HKCU` overrides. The keyboard hook therefore runs only while PotPlayer is open. Run `uninstall.ps1` to revert. It requires PotPlayer's default speed hotkeys `C`/`X` with a 0.1 step.

## License

[MIT](LICENSE)
