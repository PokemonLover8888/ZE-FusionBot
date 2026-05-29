Write-Host "Glaceon folders on Desktop:"
Write-Host ""

$glaceonFolders = Get-ChildItem 'C:\Users\ericr\OneDrive\Desktop' -Directory | Where-Object { $_.Name -like '*Glaceon*' }

foreach ($folder in $glaceonFolders) {
    Write-Host "=== $($folder.Name) ==="

    # Check for config
    $configPath = Join-Path $folder.FullName "config.json"
    if (Test-Path $configPath) {
        $lastMod = (Get-Item $configPath).LastWriteTime
        Write-Host "  Config: YES (Modified: $lastMod)"
    } else {
        Write-Host "  Config: NO"
    }

    # Check for exe
    $exe = Get-ChildItem $folder.FullName -Filter "*.exe" -ErrorAction SilentlyContinue | Where-Object { $_.Name -notmatch "createdump" } | Select-Object -First 1
    if ($exe) {
        Write-Host "  Exe: $($exe.Name)"
    } else {
        Write-Host "  Exe: NONE"
    }

    Write-Host ""
}
