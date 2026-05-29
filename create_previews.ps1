Add-Type -AssemblyName System.Drawing

function Create-EggPreview {
    param($Name, $EggUrl, $SpriteUrl)

    $wc = New-Object System.Net.WebClient

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
    $result = New-Object System.Drawing.Bitmap(260, 260)
    $g = [System.Drawing.Graphics]::FromImage($result)

    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Draw egg
    $g.DrawImage($eggImg, 20, 20, $eggSize, $eggSize)

    # Draw Pokemon at 72%
    $pokemonSize = [int]($eggSize * 0.72)
    $offset = [int](($eggSize - $pokemonSize) / 2 + 20)
    $g.DrawImage($spriteImg, $offset, $offset, $pokemonSize, $pokemonSize)

    $result.Save("$PSScriptRoot\${Name}_egg_preview.png", [System.Drawing.Imaging.ImageFormat]::Png)

    $g.Dispose()
    $result.Dispose()
    $eggImg.Dispose()
    $spriteImg.Dispose()

    Write-Host "Created $Name preview"
}

Create-EggPreview -Name "togepi" -EggUrl "https://creator.pkm-universe.com/assets/anime-eggs/175.png" -SpriteUrl "https://raw.githubusercontent.com/hexbyt3/HomeImages/master/512x512/poke_capture_0175_000_mf_n_00000000_f_n.png"

Create-EggPreview -Name "eevee" -EggUrl "https://creator.pkm-universe.com/assets/anime-eggs/133.png" -SpriteUrl "https://raw.githubusercontent.com/hexbyt3/HomeImages/master/512x512/poke_capture_0133_000_md_n_00000000_f_n.png"

Write-Host "Done! Both previews created."
