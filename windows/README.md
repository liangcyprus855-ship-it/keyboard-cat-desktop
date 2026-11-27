# 构建 Windows 版

桌宠的最低系统目标是 Windows 7 SP1，支持 x86 和 x64。Windows 7 SP1 需要 .NET Framework 4.8。Windows 10 或 11 可使用 .NET Framework 4.8 或 4.8.1。[Microsoft .NET Framework 系统要求](https://learn.microsoft.com/zh-cn/dotnet/framework/get-started/system-requirements)

安装包会先检查系统和运行时。Windows 7 用户可以在「控制面板 → 系统」查看是否已有 Service Pack 1。如果缺少 .NET Framework 4.8，按安装器提示打开微软下载页，选择「运行时」的脱机安装程序。安装完成后重新运行桌宠安装包。部署到离线电脑时，可以在另一台电脑上下载同一个微软运行时并复制过去。[微软 .NET Framework 4.8 下载页](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net48)

从源码构建请使用 Windows PowerShell 5.1，Windows 10 或 11 也可使用 PowerShell 7。构建使用 Framework 自带的 C# 编译器，生成的同一份 EXE 会随系统选择 32 位或 64 位运行。构建安装包还需要 NSIS 3，安装时保留 Modern UI 2 与简体中文语言文件。

如果系统启用了受限的脚本执行策略，请使用 `-ExecutionPolicy Bypass` 运行 PowerShell 脚本。在仓库根目录运行以下命令，同时编译桌宠和离线安装包：

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\build.ps1
```

生成的文件是 `windows/build/键盘小黑猫.exe` 和 `windows/build/键盘小黑猫-<版本>-Windows安装包.exe`（当前版本 `1.13.3`）。脚本会从 PATH 或 NSIS 的常规安装位置寻找 `makensis.exe`。需要自定义编译器位置或输出目录时可以使用下面的参数：

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\build.ps1 -Compiler '.\tools\NSIS\makensis.exe' -OutputDirectory '.\artifacts'
```

如要在构建时自动递增版本号或静默安装覆盖并启动最新版，可附加 `-Bump patch|minor` 与 `-Install` 参数：

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\build.ps1 -Bump patch -Install
```

只编译便携版时，运行下面的脚本。它只需要 .NET Framework，可以用 `-Output` 指定目标 EXE：

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\app\build.ps1
```

运行 Windows 自动化测试（包括核心状态机校验、菜单防穿透与指针隔离、自启动读写、在线更新解析与 SHA-256 校验、安装任务首运行配置校验）：

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\tests\run.ps1
```

如果修改了 `core/keycat_core.h` 或 `core/keycat_core.c`，请先同步生成 `windows/app/KeycatCore.g.cs`：

```powershell
powershell -ExecutionPolicy Bypass -File .\core\Sync-Core.ps1
```

安装包默认安装到当前账户的 `%LOCALAPPDATA%\Programs\KeycatDesktop`，创建开始菜单快捷方式，并可选择添加桌面快捷方式。首次运行（无论安装包还是便携版）均默认采用**经典黑猫**、**苹果妙控键盘**和**苹果妙控鼠标**。已有设置优先，保存在 `%LOCALAPPDATA%\KeycatDesktop\preferences.xml`。可从 Windows 的应用列表卸载，卸载时会自动清理指向安装目录的开机自启动项，并保留用户设置以便重装沿用。

右键点击桌宠或通知区域的猫脸图标，可设置：
- **小猫大小 / 角色大小**：拖动滑块可以在 1% 到 200% 之间实时缩放。左右方向键每次调节 1%，按住 Shift 再按方向键可以微调 0.1%。缩到很小时仍可从通知区域调大。
- **开机自启动**：写入或移除当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\KeycatDesktop`。
- **检查更新…**：通过 GitHub Releases 检查新版本；启动时每 24 小时自动静默检查一次。若发现新版本，安装版会下载安装程序并引导升级，便携版会在下载并校验 SHA-256 后自动替换重启。

在「角色样式」中可选用四只猫或十位同事（西装、发卡、红结、发带、圆镜、耳机、贝雷帽、棒球帽、组长、柴犬）。十位人物都能跟随鼠标和键盘操作。把光标移到人物头上，会抬手点赞；左键轻点头部，会短暂托腮。每个角色的图片和手势动画资源随便携版一并提供。
