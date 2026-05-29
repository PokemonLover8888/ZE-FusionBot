Add-Type -AssemblyName System.Drawing

$wc = New-Object System.Net.WebClient

# Download current Togepi egg
$eggBytes = $wc.DownloadData("https://creator.pkm-universe.com/assets/anime-eggs/175.png")
$eggStream = New-Object System.IO.MemoryStream(,$eggBytes)
$eggImg = [System.Drawing.Bitmap]::new($eggStream)

Write-Host "Original Togepi egg: $($eggImg.Width) x $($eggImg.Height)"

# The good positioning needed +14px right and +8px down for Pokemon
# So we need to shift the EGG LEFT by 14px and UP by 8px (opposite)
# Create a square canvas with the egg shifted to compensate

$size = [Math]::Max($eggImg.Width, $eggImg.Height) + 30  # Add padding
$result = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($result)

$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

# Shift egg LEFT 14px and UP 8px from center (to compensate for Pokemon offset)
$xOffset = (($size - $eggImg.Width) / 2) - 14   # Shift LEFT 14px
$yOffset = (($size - $eggImg.Height) / 2) - 8   # Shift UP 8px
$g.DrawImage($eggImg, $xOffset, $yOffset, $eggImg.Width, $eggImg.Height)

# Save the fixed egg
$result.Save("$PSScriptRoot\togepi_egg_fixed.png", [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host "Created fixed Togepi egg: $size x $size"
Write-Host "Saved to togepi_egg_fixed.png"

$g.Dispose()
$result.Dispose()
$eggImg.Dispose()
