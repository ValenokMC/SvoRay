# Generates the four SvoRay tray-state icons.
#
# The states must stay distinguishable at the 16-24 px system tray size and by shape
# alone, not only by colour:
#   off        - hollow grey shield
#   connecting - hollow amber shield with a centred dot
#   on         - solid green shield
#   error      - solid red shield with an exclamation mark
#
# Each icon is written as a multi-size ICO with uncompressed 32bpp BMP entries, drawn
# natively at every size instead of downscaled from one large bitmap.

param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\v2rayN\Resources')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Tray sizes only: 64 already covers 400% display scaling, and uncompressed
# 128/256 entries would bloat each icon to several hundred kilobytes.
$sizes = @(16, 20, 24, 32, 48, 64)

function New-ShieldPath {
    param([single]$Size, [single]$Inset)

    $s = $Size - (2 * $Inset)
    $points = @(
        @(0.50, 0.02), @(0.93, 0.19), @(0.93, 0.54),
        @(0.50, 0.98), @(0.07, 0.54), @(0.07, 0.19)
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $pts = New-Object System.Drawing.PointF[] $points.Count
    for ($i = 0; $i -lt $points.Count; $i++) {
        $pts[$i] = [System.Drawing.PointF]::new(
            $Inset + ($points[$i][0] * $s),
            $Inset + ($points[$i][1] * $s))
    }
    $path.AddPolygon($pts)
    $path.CloseFigure()
    return $path
}

function New-StateBitmap {
    param([int]$Size, [string]$State)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $stroke = [Math]::Max(1.0, $Size / 11.0)
    $inset = $stroke

    switch ($State) {
        'off' {
            $color = [System.Drawing.Color]::FromArgb(255, 154, 168, 184)
            $path = New-ShieldPath ([single]$Size) ([single]$inset)
            $pen = [System.Drawing.Pen]::new($color, [single]$stroke)
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $g.DrawPath($pen, $path)
            $pen.Dispose()
            $path.Dispose()
        }
        'connecting' {
            $color = [System.Drawing.Color]::FromArgb(255, 245, 185, 66)
            $path = New-ShieldPath ([single]$Size) ([single]$inset)
            $pen = [System.Drawing.Pen]::new($color, [single]$stroke)
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $g.DrawPath($pen, $path)
            $pen.Dispose()
            $path.Dispose()

            $dot = $Size * 0.30
            $brush = [System.Drawing.SolidBrush]::new($color)
            $g.FillEllipse($brush, ($Size - $dot) / 2.0, ($Size - $dot) / 2.0 - ($Size * 0.02), $dot, $dot)
            $brush.Dispose()
        }
        'on' {
            $color = [System.Drawing.Color]::FromArgb(255, 46, 204, 143)
            $path = New-ShieldPath ([single]$Size) ([single]$inset)
            $brush = [System.Drawing.SolidBrush]::new($color)
            $g.FillPath($brush, $path)
            $brush.Dispose()
            $path.Dispose()
        }
        'error' {
            $color = [System.Drawing.Color]::FromArgb(255, 229, 72, 77)
            $path = New-ShieldPath ([single]$Size) ([single]$inset)
            $brush = [System.Drawing.SolidBrush]::new($color)
            $g.FillPath($brush, $path)
            $brush.Dispose()
            $path.Dispose()

            # Exclamation mark punched out in white so the state reads by shape too.
            $markBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $barWidth = [Math]::Max(1.5, $Size * 0.13)
            $barHeight = $Size * 0.34
            $x = ($Size - $barWidth) / 2.0
            $g.FillRectangle($markBrush, $x, $Size * 0.22, $barWidth, $barHeight)
            $g.FillEllipse($markBrush, $x, $Size * 0.63, $barWidth, $barWidth)
            $markBrush.Dispose()
        }
        default { throw "Unknown state: $State" }
    }

    $g.Dispose()
    return $bitmap
}

function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)

    $maskStride = [int](([Math]::Floor(($w + 31) / 32)) * 4)
    $maskSize = $maskStride * $h

    # BITMAPINFOHEADER; the doubled height covers the XOR image plus the AND mask.
    $writer.Write([uint32]40)
    $writer.Write([int32]$w)
    $writer.Write([int32]($h * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]0)
    $writer.Write([uint32](($w * $h * 4) + $maskSize))
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)

    # 32bpp BGRA pixels, bottom-up.
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $Bitmap.GetPixel($x, $y)
            $writer.Write([byte]$p.B)
            $writer.Write([byte]$p.G)
            $writer.Write([byte]$p.R)
            $writer.Write([byte]$p.A)
        }
    }

    # AND mask: fully zero, the alpha channel already carries transparency.
    $writer.Write((New-Object byte[] $maskSize))

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    # Comma operator: without it PowerShell unrolls the array into the pipeline.
    return , $bytes
}

function Write-IconFile {
    param([string]$Path, [string]$State, [int[]]$Sizes)

    $images = @()
    foreach ($size in $Sizes) {
        $bitmap = New-StateBitmap -Size $size -State $State
        $images += , @{ Size = $size; Bytes = (Get-DibBytes -Bitmap $bitmap) }
        $bitmap.Dispose()
    }

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)

    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dim = $image.Size
        $writer.Write([byte]$(if ($dim -ge 256) { 0 } else { $dim }))
        $writer.Write([byte]$(if ($dim -ge 256) { 0 } else { $dim }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Bytes)
    }

    $writer.Dispose()
}

$resolved = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolved) | Out-Null

$map = @{
    'off'        = 'TrayOff.ico'
    'connecting' = 'TrayConnecting.ico'
    'on'         = 'TrayOn.ico'
    'error'      = 'TrayError.ico'
}

foreach ($state in $map.Keys) {
    $path = Join-Path $resolved $map[$state]
    Write-IconFile -Path $path -State $state -Sizes $sizes
    Write-Output $path
}
