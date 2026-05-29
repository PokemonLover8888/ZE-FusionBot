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

$source = 'C:\Users\ericr\source\repos\ZE-FusionBot\SysBot.Pokemon.Discord\bin\Release\net10.0\SysBot.Pokemon.Discord.dll'

foreach ($bot in $bots) {
    $dest = "C:\Users\ericr\OneDrive\Desktop\$bot"
    if (Test-Path $dest) {
        Copy-Item $source -Destination $dest -Force
        Write-Host "Deployed Discord DLL to $bot"
    } else {
        Write-Host "Folder not found: $bot"
    }
}

Write-Host "Done!"
