# 键盘小黑猫

**[官网主页与在线引导](https://liangcyprus855-ship-it.github.io/keyboard-cat-desktop/)** · **[WorkBuddy 体验入口](https://keyboard-cat.app.workbuddy.host/?sharecode=6OHA4TQODE&shareId=8b833b2c65444fb4953ce4940ffc3a2e)** · **[下载最新版安装包 (GitHub Releases)](https://github.com/liangcyprus855-ship-it/keyboard-cat-desktop/releases/latest)**

一只桌面常驻的互动小宠物，兼容 **Windows（Win7 SP1 / Win10 / Win11）** 与 **macOS 11+（Apple Silicon + Intel 通用）**。打字、点击鼠标或滚动滚轮时会在桌面角落同步动作，兼顾日常摸鱼陪伴与职场防社死体验。

## 核心特性（v1.13.2）

- **18 位角色任意切换**：
  - **4 只小猫**：经典黑猫、奶油橘猫、日本短尾三花、长毛虎斑
  - **10 位职场同事**：西装、发卡、红结、发带、圆镜、耳机、贝雷帽、棒球帽、组长、柴犬同事（支持头顶悬停点赞 / 点击托腮互动）
  - **4 只萌犬**：腊肠犬、柯基犬、黑柴犬、比熊犬
- **外观与桌搭面板**：可视化预览与一键切换 **18 位角色**、**5 套键盘**、**5 款鼠标**，以及 **4 款桌角小物件**（热咖啡、冰美式、搬砖中、勿扰牌）。
- **三步交互式新手引导**：首次启动打开三步实时试用引导页，支持当场敲键试玩、挑首发搭子及快捷键速查，后续可在右键菜单 **「新手使用引导…」** 随时重温。
- **高级精致菜单栏**：角色名片卡片、今日击键与暴走实时计数、`75%–150%` 预设滑轨与右对齐快捷键徽章。
- **职场防社死与专注助手**：
  - **一键老板键**：`Ctrl + Alt + H`（macOS：`Control + Option + H`）快速隐藏/呼出。
  - **穿透临时抓取**：开启鼠标穿透后，按住 `Alt`（macOS：`Option ⌥`）即可临时拖动或右键打开菜单。
  - **键帽隐私打码 (`••`)**：保留击键反馈与动画，隐藏具体按键字符。
  - **全屏自动隐身**（Windows）与 **45 分钟喝水提醒气泡**，并支持 **高能解压 / 柔和不晃眼** 动效切换。
- **自动更新与自启动**：内置开机自启动，以及通过 GitHub Releases（SHA-256 校验）一键检查更新。

## 从源码构建与测试

修改共享引擎 `core/` 后请先运行 `core\Sync-Core.ps1` 同步生成 `windows\app\KeycatCore.g.cs`：

| 平台 | 环境要求 | 构建命令 | 测试命令 |
| --- | --- | --- | --- |
| Windows 7 SP1 / 10 / 11 | .NET Framework 4.8 与 NSIS 3 | `powershell -ExecutionPolicy Bypass -File .\windows\build.ps1` | `powershell -ExecutionPolicy Bypass -File .\windows\tests\run.ps1` |
| macOS 11+ | Xcode Command Line Tools | `bash macos/build.sh` | `bash macos/tests/scale.sh` |

[共享核心](core/) · [Windows 源码](windows/) · [macOS 源码](macos/) · [美术资源导出工具](art-tools/) · [MIT 许可证](LICENSE)
