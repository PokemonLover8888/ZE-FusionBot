$bots = @(
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

$source = 'C:\Users\ericr\source\repos\ZE-FusionBot\publish\PKM-Universe Bot.exe'

foreach ($bot in $bots) {
    $dest = "C:\Users\ericr\OneDrive\Desktop\$bot"
    if (Test-Path $dest) {
        Copy-Item $source -Destination $dest -Force
        Write-Host "Deployed to $bot"
    } else {
        Write-Host "Folder not found: $bot"
    }
}

Write-Host "Done!"
