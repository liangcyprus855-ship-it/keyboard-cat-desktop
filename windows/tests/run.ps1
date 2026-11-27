$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$appDir = Join-Path $root 'windows\app'
$installerDir = Join-Path $root 'windows\installer'

& (Join-Path $root 'core\Sync-Core.ps1') -Check
. (Join-Path $root 'windows\toolchain.ps1')

$images = @(& (Join-Path $root 'windows\character-resources.ps1'))
$appSources = (Get-ChildItem -LiteralPath $appDir -Filter '*.cs' -File | Where-Object { $_.Name -ne 'App.cs' } | Sort-Object Name).FullName
$appEntry = Join-Path $appDir 'App.cs'
$testSource = Join-Path $PSScriptRoot 'Tests.cs'

$work = Join-Path ([IO.Path]::GetTempPath()) ('keycat-wintest-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($work)
$prevCompat = $env:__COMPAT_LAYER
try {
    $env:__COMPAT_LAYER = 'WIN7RTM'
    $targets = @()
    foreach ($pair in @(@('x86', 'Framework'), @('x64', 'Framework64'))) {
        $arch = $pair[0]; $edition = $pair[1]
        $fw = Join-Path $env:WINDIR "Microsoft.NET\$edition\v4.0.30319"
        if ((Test-Path -LiteralPath (Join-Path $fw 'csc.exe')) -and (Test-Path -LiteralPath (Join-Path $fw 'WPF\PresentationFramework.dll'))) {
            $targets += ,@($arch, $fw)
        }
    }
    if ($targets.Count -eq 0) {
        $targets += ,@('anycpu', (Get-KeycatFrameworkDirectory))
    }

    foreach ($target in $targets) {
        $arch = $target[0]; $fw = $target[1]
        $references = foreach ($name in 'PresentationCore', 'PresentationFramework', 'WindowsBase') {
            '/reference:' + (Join-Path $fw "WPF\$name.dll")
        }
        $testExe = Join-Path $work "KeycatTests-$arch.exe"
        & (Join-Path $fw 'csc.exe') /nologo /target:exe /platform:$arch /codepage:65001 /utf8output `
            /main:Tests /reference:System.Xaml.dll /reference:System.Web.Extensions.dll `
            @images ('/out:' + $testExe) @references @appSources $appEntry $testSource
        if ($LASTEXITCODE -ne 0) { throw "Windows test compilation ($arch) failed ($LASTEXITCODE)." }

        & $testExe
        if ($LASTEXITCODE -ne 0) { throw "Windows unit tests ($arch) failed ($LASTEXITCODE)." }
    }

    $framework = Get-KeycatFrameworkDirectory
    $setupExe = Join-Path $work 'SetupTasks.exe'
    & (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /r:Microsoft.CSharp.dll `
        ('/out:' + $setupExe) (Join-Path $installerDir 'SetupTasks.cs')
    if ($LASTEXITCODE -ne 0) { throw "SetupTasks compilation failed ($LASTEXITCODE)." }

    $prefsTest = Join-Path $work 'prefs-test\preferences.xml'
    $proc = Start-Process -FilePath $setupExe -ArgumentList @('first-run', $prefsTest) -Wait -PassThru
    if ($proc.ExitCode -ne 0) { throw "SetupTasks first-run failed ($($proc.ExitCode))." }
    $written = [IO.File]::ReadAllText($prefsTest, [Text.Encoding]::UTF8)
    if ($written -notmatch 'catStyle="Black"' -or $written -notmatch 'keyboard="apple"' -or $written -notmatch 'mouse="apple"') {
        throw "SetupTasks first-run defaults mismatch: $written"
    }
    Write-Output "PASS: SetupTasks first-run defaults verified ($written)"
} finally {
    if ($null -eq $prevCompat) { Remove-Item Env:\__COMPAT_LAYER -ErrorAction SilentlyContinue }
    else { $env:__COMPAT_LAYER = $prevCompat }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
