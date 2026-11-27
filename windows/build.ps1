param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'),
    [string]$Compiler,
    [ValidateSet('none', 'patch', 'minor', 'major')][string]$Bump = 'none',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
if ($Bump -ne 'none') {
    & (Join-Path $PSScriptRoot '..\core\Bump-Version.ps1') -Part $Bump
}
. (Join-Path $PSScriptRoot 'toolchain.ps1')

if ($Compiler) {
    $nsis = (Get-Item -LiteralPath $Compiler).FullName
} else {
    $nsisCommand = Get-Command makensis.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($nsisCommand) {
        $nsis = $nsisCommand.Source
    } else {
        foreach ($programs in ${env:ProgramFiles(x86)}, $env:ProgramFiles) {
            if (!$programs) { continue }
            $candidate = Join-Path $programs 'NSIS\makensis.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $nsis = $candidate; break }
        }
    }
}
if (!$nsis) { throw 'Install NSIS 3 with Modern UI 2, or pass -Compiler with the path to makensis.exe.' }

$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$payload = Join-Path $outputPath '键盘小黑猫.exe'
& (Join-Path $PSScriptRoot 'app\build.ps1') -Output $payload
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($payload)
$versionText = "$($version.FileMajorPart).$($version.FileMinorPart).$($version.FileBuildPart)"
$setup = Join-Path $outputPath "键盘小黑猫-$versionText-Windows安装包.exe"
$installer = Join-Path $PSScriptRoot 'installer'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('keycat-setup-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temporary)
$helper = Join-Path $temporary 'SetupTasks.exe'
try {
    $framework = Get-KeycatFrameworkDirectory
    & (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /r:Microsoft.CSharp.dll `
        ('/out:' + $helper) (Join-Path $installer 'SetupTasks.cs')
    if ($LASTEXITCODE -ne 0) { throw "Installer helper build failed ($LASTEXITCODE)." }
    & $nsis /V2 ('/DOUTPUT=' + $setup) ('/DPAYLOAD=' + $payload) ('/DHELPER=' + $helper) `
        ('/DCAT_ICON=' + (Join-Path $PSScriptRoot 'app\cat.ico')) `
        ('/DWIZARD=' + (Join-Path $installer 'wizard.bmp')) (Join-Path $installer 'Setup.nsi')
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed ($LASTEXITCODE)." }
} finally {
    if (Test-Path -LiteralPath $helper) { [IO.File]::Delete($helper) }
    if (Test-Path -LiteralPath $temporary) { [IO.Directory]::Delete($temporary) }
}
Get-ChildItem -LiteralPath $outputPath -Filter '键盘小黑猫-*-Windows安装包.exe' -File |
    Where-Object { $_.FullName -ne $setup } |
    Remove-Item -Force
Get-Item -LiteralPath $setup

if ($Install) {
    $installedExe = Join-Path $env:LOCALAPPDATA 'Programs\KeycatDesktop\键盘小黑猫.exe'
    Get-Process -Name '键盘小黑猫' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and ([IO.Path]::GetFullPath($_.Path) -ieq [IO.Path]::GetFullPath($installedExe))) {
                if (-not $_.CloseMainWindow() -or -not $_.WaitForExit(2000)) {
                    $_ | Stop-Process -Force
                    $_.WaitForExit(5000) | Out-Null
                }
            }
        } catch { }
    }
    $proc = Start-Process -FilePath $setup -ArgumentList '/S' -Wait -PassThru
    if ($proc.ExitCode -ne 0) { throw "Silent install failed ($($proc.ExitCode))." }
    $installedVer = [Diagnostics.FileVersionInfo]::GetVersionInfo($installedExe)
    $installedVerText = "$($installedVer.FileMajorPart).$($installedVer.FileMinorPart).$($installedVer.FileBuildPart)"
    if ($installedVerText -ne $versionText) {
        throw "Installed version mismatch: expected $versionText, got $installedVerText"
    }
    if (-not ('KeycatBuildDesktop' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class KeycatBuildDesktop {
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetUserObjectInformationW(IntPtr obj, int index, byte[] info, uint length, out uint needed);
    public static string CurrentName() {
        IntPtr d = GetThreadDesktop(GetCurrentThreadId());
        if (d == IntPtr.Zero) return String.Empty;
        byte[] buf = new byte[512];
        uint n = 0;
        if (!GetUserObjectInformationW(d, 2, buf, (uint)buf.Length, out n) || n < 2) return String.Empty;
        return Encoding.Unicode.GetString(buf, 0, Math.Min(buf.Length, (int)n)).TrimEnd('\0');
    }
}
"@
    }
    $desktopName = [KeycatBuildDesktop]::CurrentName()
    if ([Environment]::UserInteractive -and ($desktopName -ieq 'Default')) {
        Start-Process -FilePath explorer.exe -ArgumentList ('"' + $installedExe + '"')
        Write-Output "Installed and launched version $installedVerText at $installedExe"
    } else {
        Write-Output "Installed version $installedVerText at $installedExe (skipped auto-launch on isolated desktop '$desktopName' so single-instance lock is not held off-screen)"
    }
}
