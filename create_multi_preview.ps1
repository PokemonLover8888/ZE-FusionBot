Add-Type -AssemblyName System.Drawing

$wc = New-Object System.Net.WebClient

function Create-EggPreview {
    param($Name, $Species, $EggUrl, $SpriteUrl)

    try {
        # Download egg
        $eggBytes = $wc.DownloadData($EggUrl)
        $eggStream = New-Object System.IO.MemoryStream(,$eggBytes)
        $eggImg = [System.Drawing.Bitmap]::new($eggStream)

        # Download sprite
        $spriteBytes = $wc.DownloadData($SpriteUrl)
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

        # Draw egg centered
        $eggOffset = ($canvasSize - $eggSize) / 2
        $g.DrawImage($eggImg, $eggOffset, $eggOffset, $eggSize, $eggSize)

        # Draw Pokemon at 72%, centered and moved up
        $pokemonSize = [int]($eggSize * 0.72)
        $pokemonOffsetX = ($canvasSize - $pokemonSize) / 2
        $pokemonOffsetY = (($canvasSize - $pokemonSize) / 2) - 15
        $g.DrawImage($spriteImg, $pokemonOffsetX, $pokemonOffsetY, $pokemonSize, $pokemonSize)

        $result.Save("$PSScriptRoot\${Name}_egg_preview.png", [System.Drawing.Imaging.ImageFormat]::Png)

        $g.Dispose()
        $result.Dispose()
        $eggImg.Dispose()
        $spriteImg.Dispose()

        Write-Host "Created $Name preview"
    } catch {
        Write-Host "Failed $Name : $_"
    }
}

$baseEggUrl = "https://creator.pkm-universe.com/assets/anime-eggs/"
$baseSpriteUrl = "https://raw.githubusercontent.com/hexbyt3/HomeImages/master/512x512/"

# Pokemon with smooth anime eggs
Create-EggPreview -Name "elekid" -Species 239 -EggUrl "${baseEggUrl}239.png" -SpriteUrl "${baseSpriteUrl}poke_capture_0239_000_mf_n_00000000_f_n.png"
Create-EggPreview -Name "pichu" -Species 172 -EggUrl "${baseEggUrl}172.png" -SpriteUrl "${baseSpriteUrl}poke_capture_0172_000_mf_n_00000000_f_n.png"
Create-EggPreview -Name "riolu" -Species 447 -EggUrl "${baseEggUrl}447.png" -SpriteUrl "${baseSpriteUrl}poke_capture_0447_000_mf_n_00000000_f_n.png"
Create-EggPreview -Name "gible" -Species 443 -EggUrl "${baseEggUrl}443.png" -SpriteUrl "${baseSpriteUrl}poke_capture_0443_000_mf_n_00000000_f_n.png"
Create-EggPreview -Name "larvitar" -Species 246 -EggUrl "${baseEggUrl}246.png" -SpriteUrl "${baseSpriteUrl}poke_capture_0246_000_mf_n_00000000_f_n.png"
Create-EggPreview -Name "dratini" -Species 147 -EggUrl "${baseEggUrl}147.png" -SpriteUrl "${baseSpriteUrl}poke_capture_0147_000_mf_n_00000000_f_n.png"

# Pokemon using generic Eevee egg (no specific smooth egg)
Create-EggPreview -Name "charmander" -Species 4 -EggUrl "${baseEggUrl}133.png" -SpriteUrl "${baseSpriteUrl}poke_capture_0004_000_mf_n_00000000_f_n.png"
Create-EggPreview -Name "bulbasaur" -Species 1 -EggUrl "${baseEggUrl}133.png" -SpriteUrl "${baseSpriteUrl}poke_capture_0001_000_mf_n_00000000_f_n.png"

Write-Host "`nDone! Created 8 preview images."
