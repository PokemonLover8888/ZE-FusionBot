Add-Type -AssemblyName System.Drawing

$wc = New-Object System.Net.WebClient

# Download Togepi's egg
$eggBytes = $wc.DownloadData("https://creator.pkm-universe.com/assets/anime-eggs/175.png")
$eggStream = New-Object System.IO.MemoryStream(,$eggBytes)
$eggImg = [System.Drawing.Bitmap]::new($eggStream)

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

# Draw egg
$g.DrawImage($eggImg, 0, 0, $eggSize, $eggSize)

# Draw Pokemon at 72%, adjusted: moved down 8px and right 14px
$pokemonSize = [int]($eggSize * 0.72)
$pokemonX = (($eggSize - $pokemonSize) / 2) + 14   # Move right 14px
$pokemonY = (($eggSize - $pokemonSize) / 2) + 8    # Move down 8px
$g.DrawImage($spriteImg, $pokemonX, $pokemonY, $pokemonSize, $pokemonSize)

$result.Save("$PSScriptRoot\togepi_centered_preview.png", [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose()
$result.Dispose()
$eggImg.Dispose()
$spriteImg.Dispose()

Write-Host "Created Togepi preview - moved right more"
