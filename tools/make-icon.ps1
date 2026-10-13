# Genere assets\app.ico a partir de assets\logo.png (partie pictogramme, sans le texte blanc).
# A relancer uniquement si le logo change.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
$logo = [Drawing.Image]::FromFile((Join-Path $assets 'logo.png'))

# Zone imprimante + trois pictogrammes, en proportion de l'image.
$crop = New-Object Drawing.RectangleF ($logo.Width * 0.19), ($logo.Height * 0.06), ($logo.Width * 0.63), ($logo.Height * 0.63)
$sizes = 256, 128, 64, 48, 32, 24, 16
$images = @()
foreach ($size in $sizes) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $scale = [Math]::Min($size / $crop.Width, $size / $crop.Height)
    $w = $crop.Width * $scale
    $h = $crop.Height * $scale
    $dest = New-Object Drawing.RectangleF (($size - $w) / 2), (($size - $h) / 2), $w, $h
    $g.DrawImage($logo, $dest, $crop, [Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $images += , $ms.ToArray()
}
$logo.Dispose()

$out = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $out
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())
Write-Host "OK : $(Join-Path $assets 'app.ico')"
