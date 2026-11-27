# macOS 构建

在 Mac 上安装 Xcode 或 Xcode 命令行工具，然后从仓库根目录运行以下命令。

```sh
bash macos/build.sh
```

脚本会一起编译 `macos/app/*.m` 与共享核心 `core/keycat_core.c`，合并为支持 Apple 芯片与 Intel Mac（macOS 11 及以上版本）的通用应用。构建成功后，`macos/build/` 中会生成 `键盘小黑猫.app` 和对应版本的 `.pkg` 安装包。

本地构建的应用使用临时签名（ad hoc）。安装包会将应用放入「应用程序」文件夹。首次打开默认显示经典黑猫和苹果妙控键鼠，可按提示在「输入监控」中授权键盘联动。

### 升级后恢复键盘联动

临时签名的代码身份绑定到具体构建。覆盖安装后，系统可能仍显示「输入监控」已开启，但旧授权与新版应用签名不匹配，按键事件会被拒绝。1.9.1 和 1.9.2 均曾在实际设备出现此情况。[Apple 代码签名说明](https://developer.apple.com/documentation/technotes/tn3127-inside-code-signing-requirements/)

遇到这种情况时，先退出桌宠。在「系统设置 → 隐私与安全性 → 输入监控」移除旧的「键盘小黑猫」条目，再将「应用程序」里的新版加入并开启授权。重新打开桌宠，在「文本编辑」的普通文档里敲键，确认文字正常输入、桌宠同步响应。

也可在退出桌宠后，从终端运行 `tccutil reset ListenEvent local.keycat.desktop`，然后重新打开桌宠并授权。该命令只清除此应用的输入监控授权。

正式分发时，在 Mac 的钥匙串中配置同一个 Apple Developer 团队的 Developer ID Application 和 Developer ID Installer 身份，将其名称分别传给构建脚本的 `KEYCAT_APP_SIGN_IDENTITY` 和 `KEYCAT_INSTALLER_SIGN_IDENTITY`。若已在钥匙串保存公证凭据，将其配置名传给 `KEYCAT_NOTARY_PROFILE`。从临时签名切换到正式签名时，现有用户需要为正式版重新授权一次。后续升级应继续使用相同团队与应用标识，并在 Mac 上实测输入监控授权能否延续。

### 菜单功能

右击桌宠或点击菜单栏的爪印，在「角色样式」中可选择四款小猫，或西装、发卡、红结、发带、圆镜、耳机、贝雷帽、棒球帽、组长和柴犬十位同事。拖动大小滑块，可以在 1% 到 200% 之间调整当前角色。角色和大小会在下次打开时恢复。桌宠缩得太小时，仍可从菜单栏调大。

- **十位同事互动**：正立待机，键鼠活动时伸手工作；鼠标停在头部立即抬手点赞，单击头部托腮保持约 1.5 秒；按住头部拖动可移动桌宠。
- **苹果键鼠**：点「使用苹果键鼠套装」可同时换上妙控键盘和鼠标，也可单独搭配。
- **开机自启动**：在当前用户 `~/Library/LaunchAgents/local.keycat.desktop.plist` 写入或移除登录启动项。
- **检查更新…**：通过 GitHub Releases 检查新版本（启动时每 24 小时静默检查一次），发现新版时下载 `.pkg` 到「下载」文件夹（若附带 `checksums.txt` 则校验 SHA-256）并自动打开安装器。

要在带图形会话的 Mac 上验证缩放、菜单、自启动、更新解析和重启恢复，可从仓库根目录运行 `bash macos/tests/scale.sh`。测试使用独立的临时偏好设置。
