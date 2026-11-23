# 构建 Windows 版

在 Windows 10 或 11 上使用 Windows PowerShell 5.1 或 PowerShell 7。需要安装 .NET Framework 4.8 或 4.8.1，其中包含 WPF 程序集和 Framework 自带的 C# 编译器。构建安装包还需要 NSIS 3，安装时保留 Modern UI 2 与简体中文语言文件。

在仓库根目录运行以下命令，同时编译桌宠和离线安装包。

```powershell
.\windows\build.ps1
```

生成的文件是 `windows/build/键盘小黑猫.exe` 和 `windows/build/键盘小黑猫-1.8.0-Windows安装包.exe`。脚本会从 PATH 或 NSIS 的常规安装位置寻找 `makensis.exe`。需要自定义编译器位置或输出目录时可以使用下面的参数。

```powershell
.\windows\build.ps1 -Compiler '.\tools\NSIS\makensis.exe' -OutputDirectory '.\artifacts'
```

只编译便携版时，运行下面的脚本。它只需要 .NET Framework，可以用 `-Output` 指定目标 EXE。

```powershell
.\windows\app\build.ps1
```

安装包默认安装到当前账户的 `%LOCALAPPDATA%\Programs\KeycatDesktop`，创建开始菜单快捷方式，并可选择添加桌面快捷方式。首次安装时采用长毛虎斑、薄荷打字机键盘和酒红轨迹球。已有设置优先，保存在 `%LOCALAPPDATA%\KeycatDesktop\preferences.xml`。可从 Windows 的应用列表卸载，之后重新安装仍能沿用这些设置。

右键点击小猫或通知区域的猫脸图标，打开「小猫大小」。拖动滑块可以在 1% 到 200% 之间实时缩放。左右方向键每次调节 1%，按住 Shift 再按方向键可以微调 0.1%。小猫缩到很小时，仍可从通知区域调大。

在「角色样式」中可选用四只猫或「西装同事」。西装同事待机时正立，光标移到头上会竖起拇指，左键轻点头部会短暂托腮。在其他窗口移动鼠标或敲键时，她会伸手操作桌上的键鼠。便携版内嵌了三张人物姿势图。
