$source = 'C:\Users\ericr\source\repos\ZE-FusionBot\publish-sc\PKM-Universe Bot.exe'
$desktop = 'C:\Users\ericr\OneDrive\Desktop'
$botFolders = @(
    'Celebi-SWSH-Bot',
    'Diance-PLZA-Bot',
    'Flareon-LGPE-Bot',
    'Floette-PLZA-Bot',
    'Giratina-BDSP-Bot',
    'Glaceon-LGPE',
    'Hoopa-PLZA-Bot',
    'Jirachi-SWSH-Bot',
    'Landorus-PLA-Bot',
    'Meloetta-SV-Bot',
    'Mew-SV-Bot',
    'Pikachu-LGPE-Bot',
    'Rayquaza-BDSP-Bot'
)

$successCount = 0
foreach ($folder in $botFolders) {
    $dest = Join-Path $desktop $folder
    if (Test-Path $dest) {
        Copy-Item $source -Destination $dest -Force
        $copied = Get-Item (Join-Path $dest 'PKM-Universe Bot.exe')
        Write-Output "OK: $folder - Size: $($copied.Length) - Time: $($copied.LastWriteTime)"
        $successCount++
    } else {
        Write-Output "MISSING: $folder"
    }
}
Write-Output "`nDeployed to $successCount / $($botFolders.Count) bot folders"
