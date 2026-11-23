param(
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'build\键盘小黑猫.exe')
)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'toolchain.ps1')

$framework = Get-KeycatFrameworkDirectory
$assetFolder = Join-Path $PSScriptRoot '..\..\assets'
$images = foreach ($pose in '', 'Hover', 'Click') {
    $fileName = if ($pose) { 'office-colleague-' + $pose.ToLowerInvariant() } else { 'office-colleague' }
    $resourceName = if ($pose) { 'OfficeColleague.' + $pose } else { 'OfficeColleague' }
    $file = Get-Item -LiteralPath (Join-Path $assetFolder ($fileName + '.png'))
    '/resource:' + $file.FullName + ',KeycatDesktop.' + $resourceName + '.png'
}
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath))
$references = foreach ($name in 'PresentationCore', 'PresentationFramework', 'WindowsBase') {
    '/reference:' + (Join-Path $framework "WPF\$name.dll")
}
$sources = (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Sort-Object Name).FullName
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /utf8output /reference:System.Xaml.dll `
    @images `
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'desktop.manifest')) `
    ('/win32icon:' + (Join-Path $PSScriptRoot 'cat.ico')) ('/out:' + $outputPath) @references @sources
if ($LASTEXITCODE -ne 0) { throw "Application build failed ($LASTEXITCODE)." }
Get-Item -LiteralPath $outputPath
