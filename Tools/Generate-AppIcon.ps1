Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $PSScriptRoot '..\Assets'
[System.IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap 256, 256
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier(128, 22, 184, 12, 232, 54, 235, 109)
    $path.AddBezier(235, 109, 211, 81, 178, 76, 153, 91)
    $path.AddBezier(153, 91, 133, 103, 119, 122, 115, 143)
    $path.AddBezier(115, 143, 95, 124, 84, 100, 88, 79)
    $path.AddBezier(88, 79, 94, 49, 109, 32, 128, 22)
    $path.CloseFigure()
    $colors = @(
        [System.Drawing.Color]::FromArgb(245, 83, 104),
        [System.Drawing.Color]::FromArgb(20, 184, 157),
        [System.Drawing.Color]::FromArgb(59, 105, 232))
    foreach ($color in $colors) {
        $brush = New-Object System.Drawing.SolidBrush $color
        $graphics.FillPath($brush, $path)
        $brush.Dispose()
        $rotation = New-Object System.Drawing.Drawing2D.Matrix
        $rotation.RotateAt(120, [System.Drawing.PointF]::new(128, 128))
        $path.Transform($rotation)
        $rotation.Dispose()
    }
    $resized = New-Object System.Drawing.Bitmap $size, $size
    $resizeGraphics = [System.Drawing.Graphics]::FromImage($resized)
    $resizeGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $resizeGraphics.DrawImage($bitmap, 0, 0, $size, $size)
    $buffer = New-Object System.IO.MemoryStream
    $resized.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$buffer.ToArray()
    if ($size -eq 256) { $resized.Save((Join-Path $assetDirectory 'DeskCanvas.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $buffer.Dispose()
    $resizeGraphics.Dispose()
    $resized.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
$output = [System.IO.File]::Create((Join-Path $assetDirectory 'DeskCanvas.ico'))
$writer = New-Object System.IO.BinaryWriter $output
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = $sizes[$index] % 256
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
} finally {
    $writer.Dispose()
}
Write-Output "Generated DeskCanvas.ico ($($sizes -join ', ') px) and DeskCanvas.png"