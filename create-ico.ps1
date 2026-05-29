Add-Type -AssemblyName System.Drawing

$pngPath = "C:\Users\ericr\source\repos\ZE-FusionBot\SysBot.Pokemon.WinForms\Resources\pkm_pokeball.png"
$icoPath = "C:\Users\ericr\source\repos\ZE-FusionBot\SysBot.Pokemon.WinForms\icon.ico"

# Load the PNG
$png = [System.Drawing.Image]::FromFile($pngPath)

# Create multiple sizes for the icon
$sizes = @(16, 32, 48, 64, 128, 256)

# Create a memory stream for the ICO file
$ms = New-Object System.IO.MemoryStream

# ICO header
$writer = New-Object System.IO.BinaryWriter($ms)
$writer.Write([Int16]0)  # Reserved
$writer.Write([Int16]1)  # Type (1 = ICO)
$writer.Write([Int16]$sizes.Count)  # Number of images

# Calculate offsets
$headerSize = 6
$dirEntrySize = 16
$dataOffset = $headerSize + ($dirEntrySize * $sizes.Count)

$imageData = @()

foreach ($size in $sizes) {
    # Create resized bitmap
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($png, 0, 0, $size, $size)
    $g.Dispose()

    # Convert to PNG bytes
    $imgMs = New-Object System.IO.MemoryStream
    $bmp.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $imgBytes = $imgMs.ToArray()
    $imgMs.Dispose()
    $bmp.Dispose()

    $imageData += ,@{Size=$size; Data=$imgBytes}
}

# Write directory entries
$currentOffset = $dataOffset
foreach ($img in $imageData) {
    $size = $img.Size
    $data = $img.Data

    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))  # Width
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))  # Height
    $writer.Write([Byte]0)  # Color palette
    $writer.Write([Byte]0)  # Reserved
    $writer.Write([Int16]1)  # Color planes
    $writer.Write([Int16]32)  # Bits per pixel
    $writer.Write([Int32]$data.Length)  # Size of image data
    $writer.Write([Int32]$currentOffset)  # Offset to image data

    $currentOffset += $data.Length
}

# Write image data
foreach ($img in $imageData) {
    $writer.Write($img.Data)
}

$writer.Flush()

# Save to file
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())

$ms.Dispose()
$png.Dispose()

Write-Host "Created icon.ico with sizes: $($sizes -join ', ')"
