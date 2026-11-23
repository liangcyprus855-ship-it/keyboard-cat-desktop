# 重新生成 macOS 桌宠素材

macOS 安装包使用仓库里的 `macos/resources/Art`。修改猫、键盘或鼠标的矢量绘制后，可在装有 .NET Framework 4.x 和 WPF 的 Windows 上运行以下命令，更新这套素材。

```powershell
.\art-tools\Export-Art.ps1
```

若要对照新旧画面，可指定另一个输出目录。编译和渲染的临时文件位于系统临时目录。

```powershell
.\art-tools\Export-Art.ps1 -OutputDirectory '.\art-preview'
```

| 要调整的内容 | 对应源码 |
| --- | --- |
| 黑猫轮廓、姿态和橘猫花纹 | `windows/app/CatArt.cs`、`windows/app/GingerArt.cs` |
| 三花、长毛虎斑的脸形和被毛 | `windows/app/BobtailArt.cs`、`windows/app/TabbyArt.cs` |
| 五位人物的姿势、袖色与肩部位置 | `assets/office-colleague*.png`、`assets/characters/`、`assets/characters/styles.tsv` |
| 人物抬手、收手与键鼠动作 | `GestureFrames.cs`、`windows/app/OfficeColleagueArt.cs`、`macos/app/OfficeColleague.h` |
| 键盘、键位和鼠标按键外观 | `windows/app/DeskArt.cs`、`windows/app/MouseArt.cs`、`windows/app/DeskStyle.cs` |
| 导出图层、视线方向和动画采样 | `art-tools/ExportArt.cs` |
| macOS 的实时前爪绘制 | `macos/app/PetArms.h` |

`atlas.json` 记录每张透明 PNG 的文件名、逻辑边界和键位坐标。坐标原点在 480 × 320 场景左上角，头部图层使用局部坐标。PNG 以 2 倍分辨率保存。图层命名、锚点或姿态公式发生变化时，同步更新 `macos/app/PetView.m`。前爪会随实际键位和鼠标位置连续活动，改动其轮廓或花纹时应同步调整 `PetArms.h`。

修改点赞或托腮立绘后，可在 Windows PowerShell 中从仓库根目录重新生成手势起始帧。两端运行时都直接使用生成的透明 PNG。

```powershell
. .\windows\toolchain.ps1
$framework = Get-KeycatFrameworkDirectory
$wpf = Join-Path $framework WPF
$baker = Join-Path $env:TEMP ('KeycatGestureFrames-' + [guid]::NewGuid() + '.exe')
try {
    & (Join-Path $framework csc.exe) /nologo /optimize+ /r:System.Xaml.dll "/r:$wpf\WindowsBase.dll" "/r:$wpf\PresentationCore.dll" "/out:$baker" (Resolve-Path .\art-tools\GestureFrames.cs).Path
    if ($LASTEXITCODE -ne 0) { throw 'GestureFrames build failed' }
    & $baker (Resolve-Path .\assets).Path
    if ($LASTEXITCODE -ne 0) { throw 'GestureFrames export failed' }
} finally {
    if (Test-Path -LiteralPath $baker) { Remove-Item -LiteralPath $baker }
}
```
