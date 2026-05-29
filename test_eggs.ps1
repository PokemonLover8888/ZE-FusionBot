Add-Type -AssemblyName System.Drawing

$wc = New-Object System.Net.WebClient
$baseUrl = "https://creator.pkm-universe.com/assets/anime-eggs/"

# List of egg files to test
$eggs = @(37, 54, 69, 79, 92, 133, 142, 147, 155, 161, 165, 167, 170, 172, 174, 175, 194, 216, 220, 231, 238, 239, 240, 246, 258, 328, 390, 393, 410, 418, 439, 440, 443, 447, 490, 551, 636, 656, 677, 810, 816)

# Pokemon names for reference
$names = @{
    37="Vulpix"; 54="Psyduck"; 69="Bellsprout"; 79="Slowpoke"; 92="Gastly"
    133="Eevee"; 142="Aerodactyl"; 147="Dratini"; 155="Cyndaquil"; 161="Sentret"
    165="Ledyba"; 167="Spinarak"; 170="Chinchou"; 172="Pichu"; 174="Igglybuff"
    175="Togepi"; 194="Wooper"; 216="Teddiursa"; 220="Swinub"; 231="Phanpy"
    238="Smoochum"; 239="Elekid"; 240="Magby"; 246="Larvitar"; 258="Mudkip"
    328="Trapinch"; 390="Chimchar"; 393="Piplup"; 410="Shieldon"; 418="Buizel"
    439="MimeJr"; 440="Happiny"; 443="Gible"; 447="Riolu"; 490="Manaphy"
    551="Sandile"; 636="Larvesta"; 656="Froakie"; 677="Espurr"; 810="Grookey"; 816="Sobble"
}

# Create grid - 7 columns, 6 rows
$cols = 7
$rows = 6
$cellSize = 120
$padding = 5
$labelHeight = 20

$gridWidth = $cols * ($cellSize + $padding) + $padding
$gridHeight = $rows * ($cellSize + $padding + $labelHeight) + $padding

$result = New-Object System.Drawing.Bitmap($gridWidth, $gridHeight)
$g = [System.Drawing.Graphics]::FromImage($result)
$g.Clear([System.Drawing.Color]::FromArgb(40, 40, 40))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

$font = New-Object System.Drawing.Font("Arial", 8)
$brush = [System.Drawing.Brushes]::White

$i = 0
foreach ($egg in $eggs) {
    $col = $i % $cols
    $row = [math]::Floor($i / $cols)

    $x = $padding + $col * ($cellSize + $padding)
    $y = $padding + $row * ($cellSize + $padding + $labelHeight)

    try {
        $url = "${baseUrl}${egg}.png"
        $bytes = $wc.DownloadData($url)
        $stream = New-Object System.IO.MemoryStream(,$bytes)
        $img = [System.Drawing.Bitmap]::new($stream)

        $g.DrawImage($img, $x, $y, $cellSize, $cellSize)
        $img.Dispose()
        $stream.Dispose()
    } catch {
        # Draw red X for failed
        $g.DrawString("FAIL", $font, [System.Drawing.Brushes]::Red, $x + 40, $y + 50)
    }

    # Draw label
    $name = $names[$egg]
    $label = "$egg-$name"
    $g.DrawString($label, $font, $brush, $x, $y + $cellSize + 2)

    $i++
}

$g.Dispose()
$font.Dispose()

$result.Save("$PSScriptRoot\egg_test_grid.png", [System.Drawing.Imaging.ImageFormat]::Png)
$result.Dispose()

Write-Host "Created egg test grid with $($eggs.Count) eggs"
