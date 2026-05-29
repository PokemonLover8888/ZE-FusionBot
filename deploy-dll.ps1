$dllPath = "C:\Users\ericr\source\repos\ZE-FusionBot\SysBot.Pokemon.Discord\bin\Release\net10.0\SysBot.Pokemon.Discord.dll"
$botFolders = @(
    "C:\Users\ericr\OneDrive\Desktop\Celebi-SWSH-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Jirachi-SWSH-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Giratina-BDSP-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Rayquaza-BDSP-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Landorus-PLA-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Diance-PLZA-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Floette-PLZA-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Hoopa-PLZA-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Meloetta-SV-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Mew-SV-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Flareon-LGPE-Bot",
    "C:\Users\ericr\OneDrive\Desktop\Pikachu-LGPE-Bot"
)

Write-Host "Deploying SysBot.Pokemon.Discord.dll to all bot folders..."

foreach ($folder in $botFolders) {
    if (Test-Path $folder) {
        Copy-Item -Path $dllPath -Destination $folder -Force
        Write-Host "OK: $(Split-Path $folder -Leaf)"
    } else {
        Write-Host "NOT FOUND: $folder"
    }
}
Write-Host "Done!"
