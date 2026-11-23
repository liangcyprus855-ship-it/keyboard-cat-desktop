$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '..\assets'
$table = Get-Item -LiteralPath (Join-Path $assets 'characters\styles.tsv')
'/resource:' + $table.FullName + ',KeycatDesktop.Characters.tsv'
foreach ($style in (Import-Csv -LiteralPath $table.FullName -Delimiter "`t")) {
    $name = $style.style
    foreach ($pose in 'Idle', 'Hover', 'Click') {
        $suffix = $pose.ToLowerInvariant()
        $res = "$name.$pose"
        if ($name -eq 'OfficeColleague') {
            $path = if ($pose -eq 'Idle') { 'office-colleague.png' } else { "office-colleague-$suffix.png" }
            if ($pose -eq 'Idle') { $res = $name }
        } else {
            $path = 'characters\' + $name.ToLowerInvariant() + "\$suffix.png"
        }
        $file = Get-Item -LiteralPath (Join-Path $assets $path)
        '/resource:' + $file.FullName + ',KeycatDesktop.' + $res + '.png'
    }
    $directory = if ($name -eq 'OfficeColleague') { 'office' } else { $name.ToLowerInvariant() }
    foreach ($pose in 'Hover', 'Click') {
        $file = Get-Item -LiteralPath (Join-Path $assets ('characters\' + $directory + '\' + $pose.ToLowerInvariant() + '-entry.png'))
        '/resource:' + $file.FullName + ',KeycatDesktop.' + $name + '.' + $pose + 'Entry.png'
    }
}
