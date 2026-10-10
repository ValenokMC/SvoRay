# Generates every SvoRay icon: the application icon and the tray-state icons.
#
# The mark is a single ring - the "on" switch of the interface, drawn as a symbol. Tray states
# must stay distinguishable at 16-24 px and by shape alone, not only by colour:
#   off        - thin grey ring
#   connecting - amber three-quarter arc
#   on         - thick mint ring
#   game       - mint ring with a centre dot (game mode: games bypass the VPN)
#   error      - red disc with an exclamation mark
#
# Every size is drawn natively rather than downscaled from one large bitmap, so 16 px stays
# crisp. Entries up to 64 px are uncompressed 32bpp BMP (what the tray and old shells read
# best); 128 and 256 px are PNG, which every ICO consumer since Vista accepts.
#
#   pwsh -File tools\GenerateIcons.ps1

param(
    [string]$ResourceDirectory = (Join-Path $PSScriptRoot '..\src\v2rayN\Resources'),
    [string]$AssetDirectory = (Join-Path $PSScriptRoot '..\assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Graphite = [System.Drawing.Color]::FromArgb(255, 0x0F, 0x11, 0x15)
$Hairline = [System.Drawing.Color]::FromArgb(255, 0x26, 0x2A, 0x33)
$Mint = [System.Drawing.Color]::FromArgb(255, 0x3D, 0xDC, 0x97)
$Grey = [System.Drawing.Color]::FromArgb(255, 0x9A, 0xA0, 0xAD)
$Amber = [System.Drawing.Color]::FromArgb(255, 0xE0, 0xA3, 0x3A)
$Red = [System.Drawing.Color]::FromArgb(255, 0xE5, 0x53, 0x4B)

function New-Canvas {
    param([int]$Size)
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return @{ Bitmap = $bitmap; Graphics = $g }
}

function Draw-Ring {
    param($G, [float]$Size, [float]$Radius, [float]$Stroke, [System.Drawing.Color]$Color)
    $pen = [System.Drawing.Pen]::new($Color, $Stroke)
    $c = $Size / 2
    $r = $Radius - ($Stroke / 2)
    $G.DrawEllipse($pen, $c - $r, $c - $r, 2 * $r, 2 * $r)
    $pen.Dispose()
}

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $R * 2
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-AppBitmap {
    param([int]$Size)
    $canvas = New-Canvas $Size
    $g = $canvas.Graphics
    $inset = [Math]::Max(0.5, $Size * 0.03)
    $tile = New-RoundedPath $inset $inset ($Size - 2 * $inset) ($Size - 2 * $inset) ($Size * 0.22)
    $fill = [System.Drawing.SolidBrush]::new($Graphite)
    $g.FillPath($fill, $tile)
    if ($Size -ge 48) {
        $edge = [System.Drawing.Pen]::new($Hairline, [Math]::Max(1, $Size / 128))
        $g.DrawPath($edge, $tile)
        $edge.Dispose()
    }
    # Small sizes get a heavier ring: below 32 px a thin one dissolves into the tile.
    $stroke = if ($Size -le 24) { [Math]::Max(2.2, $Size * 0.15) } else { $Size * 0.115 }
    Draw-Ring $g $Size ($Size * 0.30) $stroke $Mint
    $fill.Dispose(); $tile.Dispose(); $g.Dispose()
    return $canvas.Bitmap
}

function New-TrayBitmap {
    param([int]$Size, [string]$State)
    $canvas = New-Canvas $Size
    $g = $canvas.Graphics
    $c = $Size / 2
    $outer = $c - [Math]::Max(0.5, $Size * 0.06)

    switch ($State) {
        'off' {
            Draw-Ring $g $Size $outer ([Math]::Max(1.6, $Size * 0.11)) $Grey
        }
        'connecting' {
            $stroke = [Math]::Max(2, $Size * 0.16)
            $r = $outer - $stroke / 2
            $pen = [System.Drawing.Pen]::new($Amber, $stroke)
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $g.DrawArc($pen, $c - $r, $c - $r, 2 * $r, 2 * $r, -45, 270)
            $pen.Dispose()
        }
        'on' {
            Draw-Ring $g $Size $outer ([Math]::Max(2.6, $Size * 0.22)) $Mint
        }
        'game' {
            Draw-Ring $g $Size $outer ([Math]::Max(1.8, $Size * 0.13)) $Mint
            $dot = [Math]::Max(1.6, $Size * 0.17)
            $brush = [System.Drawing.SolidBrush]::new($Mint)
            $g.FillEllipse($brush, $c - $dot, $c - $dot, 2 * $dot, 2 * $dot)
            $brush.Dispose()
        }
        'error' {
            $brush = [System.Drawing.SolidBrush]::new($Red)
            $g.FillEllipse($brush, $c - $outer, $c - $outer, 2 * $outer, 2 * $outer)
            $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $w = [Math]::Max(1.6, $Size * 0.13)
            $g.FillRectangle($white, $c - $w / 2, $Size * 0.24, $w, $Size * 0.32)
            $g.FillEllipse($white, $c - $w / 2, $Size * 0.64, $w, $w)
            $white.Dispose(); $brush.Dispose()
        }
    }
    $g.Dispose()
    return $canvas.Bitmap
}

function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $w = $Bitmap.Width; $h = $Bitmap.Height
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    $maskStride = [int](([Math]::Floor(($w + 31) / 32)) * 4)
    $maskSize = $maskStride * $h
    # BITMAPINFOHEADER; the doubled height covers the XOR image plus the AND mask.
    $writer.Write([uint32]40); $writer.Write([int32]$w); $writer.Write([int32]($h * 2))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32](($w * $h * 4) + $maskSize))
    $writer.Write([int32]0); $writer.Write([int32]0); $writer.Write([uint32]0); $writer.Write([uint32]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $Bitmap.GetPixel($x, $y)
            $writer.Write([byte]$p.B); $writer.Write([byte]$p.G); $writer.Write([byte]$p.R); $writer.Write([byte]$p.A)
        }
    }
    # AND mask stays zero: the alpha channel carries transparency.
    $writer.Write((New-Object byte[] $maskSize))
    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose(); $stream.Dispose()
    return , $bytes
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $stream = [System.IO.MemoryStream]::new()
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return , $bytes
}

function Write-IconFile {
    param([string]$Path, [int[]]$Sizes, [scriptblock]$Draw)
    $images = @()
    foreach ($size in $Sizes) {
        $bitmap = & $Draw $size
        $bytes = if ($size -ge 128) { Get-PngBytes $bitmap } else { Get-DibBytes $bitmap }
        $images += , @{ Size = $size; Bytes = $bytes }
        $bitmap.Dispose()
    }
    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dim = $image.Size
        $writer.Write([byte]$(if ($dim -ge 256) { 0 } else { $dim }))
        $writer.Write([byte]$(if ($dim -ge 256) { 0 } else { $dim }))
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
    $writer.Dispose()
    Write-Output $Path
}

$res = [System.IO.Path]::GetFullPath($ResourceDirectory)
$assets = [System.IO.Path]::GetFullPath($AssetDirectory)

Write-IconFile (Join-Path $res 'SvoRay.ico') @(16, 20, 24, 32, 40, 48, 64, 128, 256) { param($s) New-AppBitmap $s }

$tray = @{ off = 'TrayOff.ico'; connecting = 'TrayConnecting.ico'; on = 'TrayOn.ico'; game = 'TrayGame.ico'; error = 'TrayError.ico' }
foreach ($state in $tray.Keys) {
    $st = $state
    Write-IconFile (Join-Path $res $tray[$state]) @(16, 20, 24, 32, 48, 64) { param($s) New-TrayBitmap $s $st }.GetNewClosure()
}

$preview = New-AppBitmap 256
$preview.Save((Join-Path $assets 'SvoRay-icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
Write-Output (Join-Path $assets 'SvoRay-icon-preview.png')
