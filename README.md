# 键盘小黑猫

**[打开体验入口](https://keyboard-cat.app.workbuddy.host/?sharecode=6OHA4TQODE&shareId=8b833b2c65444fb4953ce4940ffc3a2e)** · [下载 Windows 和 macOS 安装包](https://github.com/liangcyprus855-ship-it/keyboard-cat-desktop/releases/latest)

右键桌宠，在「角色样式」里可选四只猫和五位同事。五位人物都能跟随键鼠操作。鼠标悬停头部时，她会抬手点赞。单击头部，她会托腮约 1.5 秒。动作之间约用 0.2 秒过渡，途中也能响应新的操作。

在同一菜单里拖动「小猫大小」或「角色大小」，即可在 1% 到 200% 之间缩放。也可以从 Windows 系统托盘或 Mac 菜单栏打开设置。

Windows 安装包兼容 Windows 7 SP1 的 32 位和 64 位版本。Win7 需要 .NET Framework 4.8；安装器发现缺少运行时时会给出下载入口。[微软 .NET Framework 4.8 运行时](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net48)

## 从源码构建

请在仓库根目录运行对应命令。

| 平台 | 准备 | 构建命令 |
| --- | --- | --- |
| Windows 10 / 11 | .NET Framework 4.8 和 NSIS 3 | `powershell -File .\windows\build.ps1` |
| macOS | Xcode 命令行工具 | `bash macos/build.sh` |

[Windows 源码](windows/) · [macOS 源码](macos/) · [美术资源导出工具](art-tools/) · [MIT 许可证](LICENSE)
