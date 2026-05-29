Add-Type -AssemblyName System.Drawing

$wc = New-Object System.Net.WebClient

# Download Togepi's actual egg
$eggBytes = $wc.DownloadData("https://creator.pkm-universe.com/assets/anime-eggs/175.png")
$eggStream = New-Object System.IO.MemoryStream(,$eggBytes)
$eggImg = [System.Drawing.Bitmap]::new($eggStream)

# Download Togepi sprite
$spriteBytes = $wc.DownloadData("https://raw.githubusercontent.com/hexbyt3/HomeImages/master/512x512/poke_capture_0175_000_mf_n_00000000_f_n.png")
$spriteStream = New-Object System.IO.MemoryStream(,$spriteBytes)
$spriteImg = [System.Drawing.Bitmap]::new($spriteStream)

# Create canvas
$eggSize = 220
$canvasSize = 260
$result = New-Object System.Drawing.Bitmap($canvasSize, $canvasSize)
$g = [System.Drawing.Graphics]::FromImage($result)

$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

# Draw egg centered on canvas
$eggOffset = ($canvasSize - $eggSize) / 2
$g.DrawImage($eggImg, $eggOffset, $eggOffset, $eggSize, $eggSize)

# Draw Pokemon at 72% size, centered horizontally, moved UP to be in egg center
$pokemonSize = [int]($eggSize * 0.72)
$pokemonOffsetX = ($canvasSize - $pokemonSize) / 2
# Move Pokemon UP by 15 pixels to center in egg (egg center is higher than canvas center)
$pokemonOffsetY = (($canvasSize - $pokemonSize) / 2) - 15
$g.DrawImage($spriteImg, $pokemonOffsetX, $pokemonOffsetY, $pokemonSize, $pokemonSize)

$result.Save("$PSScriptRoot\togepi_egg_preview.png", [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose()
$result.Dispose()
$eggImg.Dispose()
$spriteImg.Dispose()

Write-Host "Created Togepi preview - moved up to center in egg"
