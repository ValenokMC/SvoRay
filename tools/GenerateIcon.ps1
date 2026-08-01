param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\v2rayN\Resources\SvoRay.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$size = 256
$bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::Transparent)

$backgroundPath = New-RoundedPath 10 10 236 236 58
$backgroundBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 14, 20, 41))
$graphics.FillPath($backgroundBrush, $backgroundPath)

$purpleGlow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(45, 141, 123, 255))
$greenGlow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(36, 33, 221, 183))
$graphics.FillEllipse($purpleGlow, -22, -24, 142, 142)
$graphics.FillEllipse($greenGlow, 142, 145, 145, 145)

$shield = [System.Drawing.Drawing2D.GraphicsPath]::new()
$shield.AddPolygon([System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(128, 31),
    [System.Drawing.PointF]::new(208, 63),
    [System.Drawing.PointF]::new(208, 120),
    [System.Drawing.PointF]::new(208, 172),
    [System.Drawing.PointF]::new(176, 205),
    [System.Drawing.PointF]::new(128, 227),
    [System.Drawing.PointF]::new(80, 205),
    [System.Drawing.PointF]::new(48, 172),
    [System.Drawing.PointF]::new(48, 120),
    [System.Drawing.PointF]::new(48, 63)
))
$shieldBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(55, 45),
    [System.Drawing.PointF]::new(205, 220),
    [System.Drawing.Color]::FromArgb(255, 119, 121, 255),
    [System.Drawing.Color]::FromArgb(255, 38, 211, 179)
)
$graphics.FillPath($shieldBrush, $shield)

$innerShield = [System.Drawing.Drawing2D.GraphicsPath]::new()
$innerShield.AddPolygon([System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(128, 46),
    [System.Drawing.PointF]::new(193, 72),
    [System.Drawing.PointF]::new(193, 122),
    [System.Drawing.PointF]::new(193, 162),
    [System.Drawing.PointF]::new(166, 190),
    [System.Drawing.PointF]::new(128, 209),
    [System.Drawing.PointF]::new(90, 190),
    [System.Drawing.PointF]::new(63, 162),
    [System.Drawing.PointF]::new(63, 122),
    [System.Drawing.PointF]::new(63, 72)
))
$glassBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(28, 255, 255, 255))
$graphics.FillPath($glassBrush, $innerShield)

$font = [System.Drawing.Font]::new('Segoe UI', 108, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(232, 247, 255, 255))
$format = [System.Drawing.StringFormat]::new()
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$graphics.DrawString('S', $font, $textBrush, [System.Drawing.RectangleF]::new(48, 49, 160, 154), $format)

$shinePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70, 255, 255, 255), 6)
$shinePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$shinePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawArc($shinePen, 76, 49, 106, 58, 205, 103)

$pngStream = [System.IO.MemoryStream]::new()
$bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$file = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([uint16]1)
$writer.Write([uint16]32)
$writer.Write([uint32]$pngBytes.Length)
$writer.Write([uint32]22)
$writer.Write($pngBytes)
$writer.Dispose()

$shinePen.Dispose()
$format.Dispose()
$textBrush.Dispose()
$font.Dispose()
$glassBrush.Dispose()
$innerShield.Dispose()
$shieldBrush.Dispose()
$shield.Dispose()
$greenGlow.Dispose()
$purpleGlow.Dispose()
$backgroundBrush.Dispose()
$backgroundPath.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
$pngStream.Dispose()

Write-Output $resolvedOutput
