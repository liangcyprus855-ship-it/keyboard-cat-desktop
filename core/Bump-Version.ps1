param(
    [string]$Version,
    [ValidateSet('patch', 'minor', 'major')][string]$Part = 'patch'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$headerPath = Join-Path $root 'core\keycat_core.h'
$headerText = [IO.File]::ReadAllText($headerPath, [Text.Encoding]::UTF8)

if ($headerText -notmatch '#define\s+KC_APP_VERSION\s+"(\d+)\.(\d+)\.(\d+)"') {
    throw "Could not parse KC_APP_VERSION in $headerPath"
}
$maj = [int]$Matches[1]
$min = [int]$Matches[2]
$pat = [int]$Matches[3]
$oldVersion = "$maj.$min.$pat"

if ($Version) {
    if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        throw "Version must use X.Y.Z format: $Version"
    }
    $maj = [int]$Matches[1]; $min = [int]$Matches[2]; $pat = [int]$Matches[3]
} else {
    if ($Part -eq 'major') { $maj++; $min = 0; $pat = 0 }
    elseif ($Part -eq 'minor') { $min++; $pat = 0 }
    else { $pat++ }
}
$newVersion = "$maj.$min.$pat"
$newBuild = ($maj * 1000 + $min * 10 + $pat).ToString()

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Replace-Checked([string]$path, [string]$pattern, [string]$replacement) {
    $text = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
    $updated = [regex]::Replace($text, $pattern, $replacement)
    if ($updated -eq $text) { throw "Version pattern not updated in $path" }
    [IO.File]::WriteAllText($path, $updated, $utf8NoBom)
}

Replace-Checked $headerPath '(#define\s+KC_APP_VERSION\s+")\d+\.\d+\.\d+(")' "`${1}$newVersion`${2}"
Replace-Checked $headerPath '(#define\s+KC_APP_BUILD\s+")\d+(")' "`${1}$newBuild`${2}"
Replace-Checked (Join-Path $root 'windows\app\App.cs') '(\[assembly:\s*AssemblyVersion\(")\d+\.\d+\.\d+\.\d+("\)\])' "`${1}$newVersion.0`${2}"
Replace-Checked (Join-Path $root 'windows\app\desktop.manifest') '(<assemblyIdentity\s+version=")\d+\.\d+\.\d+\.\d+(")' "`${1}$newVersion.0`${2}"
Replace-Checked (Join-Path $root 'macos\Info.plist') '(<key>CFBundleShortVersionString</key>\s*<string>)\d+\.\d+\.\d+(</string>)' "`${1}$newVersion`${2}"
Replace-Checked (Join-Path $root 'macos\Info.plist') '(<key>CFBundleVersion</key>\s*<string>)\d+(</string>)' "`${1}$newBuild`${2}"
Replace-Checked (Join-Path $root 'macos\installer\distribution.xml') '(<pkg-ref[^>]*\bversion=")\d+\.\d+\.\d+(")' "`${1}$newVersion`${2}"
Replace-Checked (Join-Path $root 'windows\README.md') '(当前版本 `)\d+\.\d+\.\d+(`)' "`${1}$newVersion`${2}"

& (Join-Path $PSScriptRoot 'Sync-Core.ps1')

Write-Output "Bumped version: $oldVersion -> $newVersion (build $newBuild)"
