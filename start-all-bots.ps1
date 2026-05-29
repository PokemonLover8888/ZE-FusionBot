# Start all bot programs
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
    "C:\Users\ericr\OneDrive\Desktop\Glaceon-LGPE",
    "C:\Users\ericr\OneDrive\Desktop\Pikachu-LGPE-Bot"
)

Write-Host "Starting all bot programs..."

foreach ($folder in $botFolders) {
    if (Test-Path $folder) {
        $exe = Get-ChildItem $folder -Filter "*.exe" | Where-Object { $_.Name -notmatch "createdump" } | Select-Object -First 1
        if ($exe) {
            Write-Host "Starting: $($exe.Name) from $(Split-Path $folder -Leaf)"
            Start-Process -FilePath $exe.FullName -WorkingDirectory $folder
            Start-Sleep -Milliseconds 500
        } else {
            Write-Host "No exe found in: $(Split-Path $folder -Leaf)"
        }
    } else {
        Write-Host "Folder not found: $folder"
    }
}

Write-Host "Done! All bots started."
