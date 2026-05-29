$source = "C:\Users\ericr\source\repos\ZE-FusionBot\publish-sc\PKM-Universe Bot.exe"
$desktop = "C:\Users\ericr\OneDrive\Desktop"

$botFolders = @(
    "Celebi-SWSH-Bot",
    "Diance-PLZA-Bot",
    "Flareon-LGPE-Bot",
    "Floette-PLZA-Bot",
    "Giratina-BDSP-Bot",
    "Glaceon-LGPE",
    "Hoopa-PLZA-Bot",
    "Jirachi-SWSH-Bot",
    "Landorus-PLA-Bot",
    "Meloetta-SV-Bot",
    "Mew-SV-Bot",
    "Pikachu-LGPE-Bot",
    "Rayquaza-BDSP-Bot"
)

$sourceSize = (Get-Item $source).Length
Write-Host "Source exe: $([math]::Round($sourceSize / 1MB, 1)) MB"
Write-Host ""

$success = 0
$failed = 0

foreach ($folder in $botFolders) {
    $dest = Join-Path $desktop $folder
    if (Test-Path $dest) {
        try {
            Copy-Item $source -Destination (Join-Path $dest "PKM-Universe Bot.exe") -Force
            Write-Host "[OK] $folder" -ForegroundColor Green
            $success++
        } catch {
            Write-Host "[FAIL] $folder - $($_.Exception.Message)" -ForegroundColor Red
            $failed++
        }
    } else {
        Write-Host "[SKIP] $folder - folder not found" -ForegroundColor Yellow
        $failed++
    }
}

Write-Host ""
Write-Host "Done: $success deployed, $failed failed/skipped"
