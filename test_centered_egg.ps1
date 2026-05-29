Add-Type -AssemblyName System.Drawing

$wc = New-Object System.Net.WebClient

# Load the centered Togepi egg
$eggImg = [System.Drawing.Bitmap]::new("$PSScriptRoot\togepi_egg_centered.png")

# Download Togepi sprite
$spriteBytes = $wc.DownloadData("https://raw.githubusercontent.com/hexbyt3/HomeImages/master/512x512/poke_capture_0175_000_mf_n_00000000_f_n.png")
$spriteStream = New-Object System.IO.MemoryStream(,$spriteBytes)
$spriteImg = [System.Drawing.Bitmap]::new($spriteStream)

# Create canvas
$eggSize = 220
$result = New-Object System.Drawing.Bitmap($eggSize, $eggSize)
$g = [System.Drawing.Graphics]::FromImage($result)

$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

# Draw egg (now square and centered)
$g.DrawImage($eggImg, 0, 0, $eggSize, $eggSize)

# Draw Pokemon with STANDARD centering (no offset)
$pokemonSize = [int]($eggSize * 0.72)
$pokemonX = ($eggSize - $pokemonSize) / 2
$pokemonY = ($eggSize - $pokemonSize) / 2
$g.DrawImage($spriteImg, $pokemonX, $pokemonY, $pokemonSize, $pokemonSize)

$result.Save("$PSScriptRoot\togepi_fixed_preview.png", [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose()
$result.Dispose()
$eggImg.Dispose()
$spriteImg.Dispose()

Write-Host "Created preview with fixed centered egg"
