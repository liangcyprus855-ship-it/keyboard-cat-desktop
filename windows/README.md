# 构建 Windows 版

桌宠的最低系统目标是 Windows 7 SP1，支持 x86 和 x64。Windows 7 SP1 需要 .NET Framework 4.8。Windows 10 或 11 可使用 .NET Framework 4.8 或 4.8.1。[Microsoft .NET Framework 系统要求](https://learn.microsoft.com/zh-cn/dotnet/framework/get-started/system-requirements)

安装包会先检查系统和运行时。Windows 7 用户可以在「控制面板 → 系统」查看是否已有 Service Pack 1。如果缺少 .NET Framework 4.8，按安装器提示打开微软下载页，选择「运行时」的脱机安装程序。安装完成后重新运行桌宠安装包。部署到离线电脑时，可以在另一台电脑上下载同一个微软运行时并复制过去。[微软 .NET Framework 4.8 下载页](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net48)

从源码构建请使用 Windows PowerShell 5.1，Windows 10 或 11 也可使用 PowerShell 7。构建使用 Framework 自带的 C# 编译器，生成的同一份 EXE 会随系统选择 32 位或 64 位运行。构建安装包还需要 NSIS 3，安装时保留 Modern UI 2 与简体中文语言文件。

在仓库根目录运行以下命令，同时编译桌宠和离线安装包。

```powershell
.\windows\build.ps1
```

生成的文件是 `windows/build/键盘小黑猫.exe` 和 `windows/build/键盘小黑猫-1.9.0-Windows安装包.exe`。脚本会从 PATH 或 NSIS 的常规安装位置寻找 `makensis.exe`。需要自定义编译器位置或输出目录时可以使用下面的参数。

```powershell
.\windows\build.ps1 -Compiler '.\tools\NSIS\makensis.exe' -OutputDirectory '.\artifacts'
```

只编译便携版时，运行下面的脚本。它只需要 .NET Framework，可以用 `-Output` 指定目标 EXE。

```powershell
.\windows\app\build.ps1
```

安装包默认安装到当前账户的 `%LOCALAPPDATA%\Programs\KeycatDesktop`，创建开始菜单快捷方式，并可选择添加桌面快捷方式。首次安装时采用长毛虎斑、薄荷打字机键盘和酒红轨迹球。已有设置优先，保存在 `%LOCALAPPDATA%\KeycatDesktop\preferences.xml`。可从 Windows 的应用列表卸载，之后重新安装仍能沿用这些设置。

右键点击小猫或通知区域的猫脸图标，打开「小猫大小」。拖动滑块可以在 1% 到 200% 之间实时缩放。左右方向键每次调节 1%，按住 Shift 再按方向键可以微调 0.1%。小猫缩到很小时，仍可从通知区域调大。

在「角色样式」中可选用四只猫或五位同事。五位人物都能跟随鼠标和键盘操作。把光标移到人物头上，她会抬手点赞；左键轻点头部，她会短暂托腮。每个角色的图片和手势动画资源随便携版一并提供。
