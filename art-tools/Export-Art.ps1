[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../macos/resources/Art')
)
$ErrorActionPreference = 'Stop'

$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../windows/app'))
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The sprite exporter requires Windows with .NET Framework 4.x and WPF.'
}

$references = @('WindowsBase', 'PresentationCore', 'PresentationFramework') | ForEach-Object {
    $dll = Get-Item -LiteralPath (Join-Path $framework ('WPF/' + $_ + '.dll'))
    '/reference:' + $dll.FullName
}
$sources = @('DeskStyle', 'DeskArt', 'MouseArt', 'CatArt', 'GingerArt', 'BobtailArt', 'TabbyArt', 'OfficeColleagueArt') | ForEach-Object {
    (Get-Item -LiteralPath (Join-Path $source ($_ + '.cs'))).FullName
}
$exportSource = (Get-Item -LiteralPath (Join-Path $PSScriptRoot 'ExportArt.cs')).FullName
$character = (Get-Item -LiteralPath (Join-Path $PSScriptRoot '../assets/office-colleague.png')).FullName
$hover = (Get-Item -LiteralPath (Join-Path $PSScriptRoot '../assets/office-colleague-hover.png')).FullName
$click = (Get-Item -LiteralPath (Join-Path $PSScriptRoot '../assets/office-colleague-click.png')).FullName

$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $tempParent ('keycat-art-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
try {
    $compilerOutput = Join-Path $work 'ExportArt.exe'
    $characterResources = @(
        '/resource:' + $character + ',KeycatDesktop.OfficeColleague.png'
        '/resource:' + $hover + ',KeycatDesktop.OfficeColleague.Hover.png'
        '/resource:' + $click + ',KeycatDesktop.OfficeColleague.Click.png'
    )
    & $compiler /nologo /optimize+ /codepage:65001 /reference:System.Xaml.dll /reference:System.Web.Extensions.dll ('/out:' + $compilerOutput) @characterResources @references @sources $exportSource
    if ($LASTEXITCODE -ne 0) { throw "Artwork exporter compilation failed ($LASTEXITCODE)." }

    $stage = Join-Path $work 'Art'
    & $compilerOutput $stage
    if ($LASTEXITCODE -ne 0) { throw "Artwork rendering failed ($LASTEXITCODE)." }

    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach ($image in Get-ChildItem -LiteralPath $stage -Filter '*.png' -File) {
        [IO.File]::Copy($image.FullName, (Join-Path $destination $image.Name), $true)
    }
    [IO.File]::Copy((Join-Path $stage 'atlas.json'), (Join-Path $destination 'atlas.json'), $true)
    Write-Output "Assets generated in $destination"
} finally {
    $resolved = (Resolve-Path -LiteralPath $work).ProviderPath
    $parent = [IO.Path]::GetDirectoryName($resolved)
    $leaf = [IO.Path]::GetFileName($resolved)
    if ($parent.TrimEnd([IO.Path]::DirectorySeparatorChar) -eq $tempParent.TrimEnd([IO.Path]::DirectorySeparatorChar) -and
        $leaf -match '^keycat-art-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
