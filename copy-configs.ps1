# Create bot-configs folder
$configDir = "C:\Users\ericr\source\repos\ZE-FusionBot\bot-configs"
New-Item -ItemType Directory -Force -Path $configDir | Out-Null

$botFolders = @(
    "Celebi-SWSH-Bot",
    "Jirachi-SWSH-Bot",
    "Giratina-BDSP-Bot",
    "Rayquaza-BDSP-Bot",
    "Landorus-PLA-Bot",
    "Diance-PLZA-Bot",
    "Floette-PLZA-Bot",
    "Hoopa-PLZA-Bot",
    "Meloetta-SV-Bot",
    "Mew-SV-Bot",
    "Flareon-LGPE-Bot",
    "Pikachu-LGPE-Bot"
)

Write-Host "Copying configs from Desktop bot folders to source repo..."

foreach ($bot in $botFolders) {
    $src = "C:\Users\ericr\OneDrive\Desktop\$bot\config.json"
    $dest = "$configDir\$bot-config.json"

    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $dest -Force
        Write-Host "OK: $bot"
    } else {
        Write-Host "NOT FOUND: $bot"
    }
}

Write-Host ""
Write-Host "Configs saved to: $configDir"
Write-Host "Done!"
