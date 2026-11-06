# Draws the app icon (a subwoofer cone with a silver sine wave through it) and writes Source\Desktop\SubWake.ico.
# Sizes 16-128 are stored as classic bitmaps (best for tray and Explorer), 256 as PNG.
param([string]$OutFile = (Join-Path (Split-Path $PSScriptRoot) 'Source\Desktop\SubWake.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Background
    $r = [Math]::Max(3, $S * 0.22); $inset = [Math]::Max(0.5, $S * 0.02)
    $rect = New-Object System.Drawing.RectangleF $inset, $inset, ($S - 2 * $inset), ($S - 2 * $inset)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 40, 43, 49)), ([System.Drawing.Color]::FromArgb(255, 14, 15, 18)), 45.0
    $g.FillPath($bg, $path)
    if ($S -ge 32) {
        $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), ([Math]::Max(1, $S / 64))
        $g.DrawPath($border, $path)
    }

    # Subwoofer cone seen from the front, on a 64-unit grid: silver surround, dark cone.
    $k = $S / 64.0
    $silverRect = New-Object System.Drawing.RectangleF 0, 0, $S, $S
    $silver = New-Object System.Drawing.Drawing2D.LinearGradientBrush $silverRect, ([System.Drawing.Color]::FromArgb(255, 242, 244, 248)), ([System.Drawing.Color]::FromArgb(255, 150, 162, 178)), 45.0
    $c = 32 * $k
    $r1 = 21 * $k
    $surround = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 35, 38, 43))
    $g.FillEllipse($surround, ($c - $r1), ($c - $r1), (2 * $r1), (2 * $r1))
    $ringPen = New-Object System.Drawing.Pen $silver, ([Math]::Max(1.2, 3.5 * $k))
    $g.DrawEllipse($ringPen, ($c - $r1), ($c - $r1), (2 * $r1), (2 * $r1))
    $r2 = 13.5 * $k
    $cone = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 20, 23))
    $g.FillEllipse($cone, ($c - $r2), ($c - $r2), (2 * $r2), (2 * $r2))
    if ($S -ge 32) {
        $conePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(34, 255, 255, 255)), ([Math]::Max(1, $k))
        $g.DrawEllipse($conePen, ($c - $r2), ($c - $r2), (2 * $r2), (2 * $r2))
    }

    # A low sine wave (two cycles) through the cone, outlined in the tile colour so it stands off the ring.
    $points = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    $n = [Math]::Max(64, $S * 2)
    for ($i = 0; $i -le $n; $i++) {
        $t = $i / $n
        $x = (6 + 52 * $t) * $k
        $y = (32 - 8.25 * [Math]::Sin(2 * [Math]::PI * 2 * $t)) * $k
        $points.Add((New-Object System.Drawing.PointF $x, $y))
    }
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 23, 25, 29)), ([Math]::Max(2.6, 7 * $k))
    $outline.StartCap = 'Round'; $outline.EndCap = 'Round'; $outline.LineJoin = 'Round'
    $g.DrawLines($outline, $points.ToArray())
    $pen = New-Object System.Drawing.Pen $silver, ([Math]::Max(1.4, 3.2 * $k))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLines($pen, $points.ToArray())
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $S = $bmp.Width
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $S, $S), 'ReadOnly', ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $pixels = New-Object byte[] ($data.Stride * $S)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)
    $maskRow = [int]([Math]::Floor(($S + 31) / 32) * 4)
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$S); $w.Write([int]($S * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($S * $S * 4 + $maskRow * $S)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $S - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $data.Stride, $S * 4) }   # Bottom-up rows
    $w.Write((New-Object byte[] ($maskRow * $S)))                                          # Empty AND mask; alpha is used
    $w.Flush()
    return , $ms.ToArray()   # The comma keeps PowerShell from unrolling the byte array.
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($S in $sizes) {
    $bmp = New-IconBitmap $S
    if ($S -eq 256) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $images.Add($ms.ToArray()) }
    else { $images.Add([byte[]](Get-DibBytes $bmp)) }
    $bmp.Dispose()
}

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $S = $sizes[$i]; $dim = if ($S -ge 256) { 0 } else { $S }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes($OutFile, $out.ToArray())
Write-Output "Wrote $OutFile ($($out.Length) bytes, sizes $($sizes -join ', '))"
