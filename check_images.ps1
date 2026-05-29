Add-Type -AssemblyName System.Drawing

$wc = New-Object System.Net.WebClient

# Download and save Togepi egg for inspection
$eggBytes = $wc.DownloadData("https://creator.pkm-universe.com/assets/anime-eggs/175.png")
[System.IO.File]::WriteAllBytes("$PSScriptRoot\togepi_egg_check.png", $eggBytes)
$eggStream = New-Object System.IO.MemoryStream(,$eggBytes)
$eggImg = [System.Drawing.Bitmap]::new($eggStream)
Write-Host "Togepi Egg: $($eggImg.Width) x $($eggImg.Height)"

# Download and save Elekid egg for comparison
$elekidBytes = $wc.DownloadData("https://creator.pkm-universe.com/assets/anime-eggs/239.png")
[System.IO.File]::WriteAllBytes("$PSScriptRoot\elekid_egg_check.png", $elekidBytes)
$elekidStream = New-Object System.IO.MemoryStream(,$elekidBytes)
$elekidImg = [System.Drawing.Bitmap]::new($elekidStream)
Write-Host "Elekid Egg: $($elekidImg.Width) x $($elekidImg.Height)"

$eggImg.Dispose()
$elekidImg.Dispose()

Write-Host "Saved both eggs for visual comparison"
