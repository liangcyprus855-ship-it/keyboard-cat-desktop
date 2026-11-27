$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '..\assets'
$table = Get-Item -LiteralPath (Join-Path $assets 'characters\styles.tsv')
'/resource:' + $table.FullName + ',KeycatDesktop.Characters.tsv'
foreach ($style in (Import-Csv -LiteralPath $table.FullName -Delimiter "`t")) {
    $name = $style.style
    foreach ($pose in 'Idle', 'Work', 'Sleep', 'Hover', 'Click') {
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
}
$petTable = Get-Item -LiteralPath (Join-Path $assets 'pets\styles.tsv')
'/resource:' + $petTable.FullName + ',KeycatDesktop.Pets.tsv'
foreach ($pet in (Import-Csv -LiteralPath $petTable.FullName -Delimiter "`t")) {
    $name = $pet.style
    $slug = $pet.slug
    foreach ($part in 'head', 'body', 'ruff', 'tail', 'pupilL', 'pupilR', 'sleeveL', 'pawL', 'sleeveR', 'pawR') {
        $file = Get-Item -LiteralPath (Join-Path $assets "pets\$slug\$part.png")
        '/resource:' + $file.FullName + ',KeycatDesktop.Pet.' + $name + '.' + $part + '.png'
    }
}
