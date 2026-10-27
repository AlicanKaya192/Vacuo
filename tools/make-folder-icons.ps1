# Draws the folder icons Organize gives the folders it makes (stage 3h) and writes them to
# src\Vacuo.App\Assets\FolderIcons as .ico files with a PNG frame for every size Windows asks for.
# Every size is drawn on its own, so the small ones keep thick lines instead of a blurred copy of 256.
# The glyphs are our own drawings in a 100 x 100 box; no font or icon pack is used.
#
#   powershell -NoProfile -File tools\make-folder-icons.ps1 [-Preview <path to a .png sheet>]

param(
    [string]$Preview = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'src\Vacuo.App\Assets\FolderIcons'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

# VOID palette (src\Vacuo.App\App.xaml).
function Color([string]$hex, [int]$alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}
$accent = Color '#9D7CFF'
$light = Color '#B39BFF'
$glyphColor = Color '#E2D8FF'

function Add-RoundRect($path, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
}

function New-RoundPen($color, [float]$width) {
    $pen = New-Object System.Drawing.Pen($color, $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen
}

function Points([float[]]$xy) {
    $points = @()
    for ($i = 0; $i -lt $xy.Length; $i += 2) { $points += New-Object System.Drawing.PointF($xy[$i], $xy[$i + 1]) }
    [System.Drawing.PointF[]]$points
}

# Each glyph draws in a 100 x 100 box with $pen (lines) and $brush (solid parts). $detail is false
# below 32 px, where thin inner lines would only blur.
$glyphs = [ordered]@{
    documents = {
        param($g, $pen, $brush, $detail)
        $g.DrawPolygon($pen, (Points 22,8, 62,8, 80,26, 80,92, 22,92))
        $g.DrawLines($pen, (Points 62,8, 62,26, 80,26))
        if ($detail) {
            $g.DrawLine($pen, 34, 46, 68, 46); $g.DrawLine($pen, 34, 60, 68, 60); $g.DrawLine($pen, 34, 74, 56, 74)
        }
    }
    images = {
        param($g, $pen, $brush, $detail)
        $body = New-Object System.Drawing.Drawing2D.GraphicsPath; Add-RoundRect $body 8 30 84 58 12
        $g.DrawPath($pen, $body)
        $g.DrawLines($pen, (Points 32,30, 38,17, 62,17, 68,30))
        $g.DrawEllipse($pen, 33, 42, 34, 34)
        if ($detail) { $g.FillEllipse($brush, 76, 38, 7, 7) }
    }
    videos = {
        param($g, $pen, $brush, $detail)
        $body = New-Object System.Drawing.Drawing2D.GraphicsPath; Add-RoundRect $body 8 18 84 64 12
        $g.DrawPath($pen, $body)
        $g.FillPolygon($brush, (Points 40,34, 40,66, 66,50))
        if ($detail) { $g.DrawLine($pen, 30, 94, 70, 94) }
    }
    music = {
        param($g, $pen, $brush, $detail)
        $g.DrawLine($pen, 38, 26, 38, 76); $g.DrawLine($pen, 80, 16, 80, 66)
        $g.DrawLine($pen, 38, 26, 80, 16)
        if ($detail) { $g.DrawLine($pen, 38, 40, 80, 30) }
        $g.FillEllipse($brush, 16, 66, 24, 20); $g.FillEllipse($brush, 58, 56, 24, 20)
    }
    archives = {
        param($g, $pen, $brush, $detail)
        $lid = New-Object System.Drawing.Drawing2D.GraphicsPath; Add-RoundRect $lid 8 16 84 20 6
        $box = New-Object System.Drawing.Drawing2D.GraphicsPath; Add-RoundRect $box 15 36 70 52 6
        $g.DrawPath($pen, $lid); $g.DrawPath($pen, $box)
        $g.DrawLine($pen, 40, 54, 60, 54)
    }
    installers = {
        param($g, $pen, $brush, $detail)
        $g.DrawLine($pen, 50, 8, 50, 58)
        $g.DrawLines($pen, (Points 30,40, 50,60, 70,40))
        $g.DrawLines($pen, (Points 12,62, 12,88, 88,88, 88,62))
    }
    code = {
        param($g, $pen, $brush, $detail)
        $g.DrawLines($pen, (Points 32,24, 8,50, 32,76))
        $g.DrawLines($pen, (Points 68,24, 92,50, 68,76))
        if ($detail) { $g.DrawLine($pen, 57, 18, 43, 82) }
    }
    invoices = {
        param($g, $pen, $brush, $detail)
        $g.DrawPolygon($pen, (Points 20,8, 80,8, 80,92, 70,84, 60,92, 50,84, 40,92, 30,84, 20,92))
        $g.DrawLine($pen, 32, 28, 68, 28)
        if ($detail) { $g.DrawLine($pen, 32, 44, 68, 44); $g.DrawLine($pen, 32, 60, 54, 60) }
    }
    school = {
        param($g, $pen, $brush, $detail)
        $g.DrawPolygon($pen, (Points 50,14, 94,36, 50,58, 6,36))
        $g.DrawLine($pen, 24, 46, 24, 68)
        $g.DrawBezier($pen, 24, 68, 36, 84, 64, 84, 76, 68)
        $g.DrawLine($pen, 76, 68, 76, 46)
        if ($detail) { $g.DrawLine($pen, 94, 36, 94, 64) }
    }
    work = {
        param($g, $pen, $brush, $detail)
        $body = New-Object System.Drawing.Drawing2D.GraphicsPath; Add-RoundRect $body 6 30 88 58 10
        $g.DrawPath($pen, $body)
        $g.DrawLines($pen, (Points 35,30, 35,16, 65,16, 65,30))
        if ($detail) { $g.DrawLine($pen, 6, 56, 94, 56); $g.FillRectangle($brush, 43, 49, 14, 14) }
    }
    travel = {
        param($g, $pen, $brush, $detail)
        $plane = Points 50,4, 57,12, 57,38, 94,60, 94,70, 57,58, 57,80, 70,90, 70,97, 50,91, 30,97, 30,90, 43,80, 43,58, 6,70, 6,60, 43,38, 43,12
        $g.FillPolygon($brush, $plane)
        $g.DrawPolygon($pen, $plane)
    }
    games = {
        param($g, $pen, $brush, $detail)
        $pad = New-Object System.Drawing.Drawing2D.GraphicsPath
        $pad.AddLine(28, 26, 72, 26)
        $pad.AddBezier(72, 26, 87, 26, 95, 40, 97, 56)
        $pad.AddBezier(97, 56, 99, 73, 92, 84, 83, 84)
        $pad.AddBezier(83, 84, 75, 84, 71, 73, 65, 66)
        $pad.AddLine(65, 66, 35, 66)
        $pad.AddBezier(35, 66, 29, 73, 25, 84, 17, 84)
        $pad.AddBezier(17, 84, 8, 84, 1, 73, 3, 56)
        $pad.AddBezier(3, 56, 5, 40, 13, 26, 28, 26)
        $pad.CloseFigure()
        $g.DrawPath($pen, $pad)
        $g.DrawLine($pen, 20, 46, 36, 46); $g.DrawLine($pen, 28, 38, 28, 54)
        if ($detail) { $g.FillEllipse($brush, 64, 37, 9, 9); $g.FillEllipse($brush, 74, 47, 9, 9) }
    }
    screenshots = {
        param($g, $pen, $brush, $detail)
        $g.DrawLines($pen, (Points 8,30, 8,8, 30,8)); $g.DrawLines($pen, (Points 70,8, 92,8, 92,30))
        $g.DrawLines($pen, (Points 92,70, 92,92, 70,92)); $g.DrawLines($pen, (Points 30,92, 8,92, 8,70))
        if ($detail) { $g.DrawLines($pen, (Points 28,70, 44,50, 56,62, 64,54, 74,70)) }
    }
}

function Draw-Icon([string]$name, [int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256, $size / 256)
    $pixel = 256 / $size
    $small = $size -lt 32

    # The folder: a back sheet with its tab, the front sheet over it, a violet rim and a lit top edge.
    # Winding, so where the tab and the sheet overlap stays filled.
    $back = New-Object System.Drawing.Drawing2D.GraphicsPath([System.Drawing.Drawing2D.FillMode]::Winding)
    Add-RoundRect $back 14 28 100 46 16
    Add-RoundRect $back 14 46 228 176 22
    $backBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 28)), (New-Object System.Drawing.PointF(0, 222)), (Color '#3A2C63'), (Color '#1C1530'))
    $g.FillPath($backBrush, $back)

    $front = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundRect $front 14 76 228 156 22
    $frontBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 76)), (New-Object System.Drawing.PointF(0, 232)), (Color '#2E2350'), (Color '#100C1C'))
    $g.FillPath($frontBrush, $front)
    $rim = New-Object System.Drawing.Pen($accent, [Math]::Max(5, 1.0 * $pixel))
    $g.DrawPath($rim, $front)
    if (-not $small) {
        $g.DrawLine((New-RoundPen (Color '#9D7CFF' 160) 4), 30, 30, 98, 30)
        $g.DrawLine((New-RoundPen $light ([Math]::Max(4, 0.8 * $pixel))), 44, 84, 212, 84)
    }

    # The glyph on the front sheet: bigger and bolder when small; a soft violet glow behind it when large.
    $box = if ($small) { 128 } else { 104 }
    $state = $g.Save()
    $g.TranslateTransform(128 - $box / 2, 156 - $box / 2)
    $g.ScaleTransform($box / 100, $box / 100)
    $stroke = if ($small) { [Math]::Max(13, 1.6 * $pixel * 100 / $box) } else { 8 }
    if (-not $small) {
        & $glyphs[$name] $g (New-RoundPen (Color '#9D7CFF' 70) ($stroke + 12)) (New-Object System.Drawing.SolidBrush((Color '#9D7CFF' 70))) $true
    }
    & $glyphs[$name] $g (New-RoundPen $glyphColor $stroke) (New-Object System.Drawing.SolidBrush($glyphColor)) (-not $small)
    $g.Restore($state)

    $g.Dispose()
    $bitmap
}

function PngBytes($bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $stream.ToArray()
}

# ICONDIR, one ICONDIRENTRY per frame, then the PNG frames (Vista and later read PNG frames).
function Write-Icon([string]$path, [byte[][]]$frames, [int[]]$frameSizes) {
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Length)
    $offset = 6 + 16 * $frames.Length
    for ($i = 0; $i -lt $frames.Length; $i++) {
        $edge = if ($frameSizes[$i] -ge 256) { 0 } else { $frameSizes[$i] }
        $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($path, $stream.ToArray())
}

New-Item -ItemType Directory -Force $output | Out-Null
foreach ($name in $glyphs.Keys) {
    $frames = foreach ($size in $sizes) { , (PngBytes (Draw-Icon $name $size)) }
    Write-Icon (Join-Path $output "$name.ico") $frames $sizes
    Write-Output "Icon: $name.ico"
}

# The preview sheet: every icon at 16, 32, 48 and 128 px on a dark and a light Explorer background.
if ($Preview) {
    $previewSizes = 16, 32, 48, 128
    $cell = 150
    $sheet = New-Object System.Drawing.Bitmap -ArgumentList ($cell * $glyphs.Count), 600
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $font = New-Object System.Drawing.Font('Segoe UI', 10)
    foreach ($band in @(@{ Top = 0; Back = '#202020'; Text = '#E0E0E0' }, @{ Top = 300; Back = '#F3F3F3'; Text = '#202020' })) {
        $g.FillRectangle((New-Object System.Drawing.SolidBrush((Color $band.Back))), 0, $band.Top, $sheet.Width, 300)
        $column = 0
        foreach ($name in $glyphs.Keys) {
            $x = $column * $cell + 11
            $g.DrawImage((Draw-Icon $name 128), $x, $band.Top + 14, 128, 128)
            $g.DrawImage((Draw-Icon $name 48), $x, $band.Top + 160, 48, 48)
            $g.DrawImage((Draw-Icon $name 32), $x + 60, $band.Top + 168, 32, 32)
            $g.DrawImage((Draw-Icon $name 16), $x + 104, $band.Top + 176, 16, 16)
            $g.DrawString($name, $font, (New-Object System.Drawing.SolidBrush((Color $band.Text))), $x, $band.Top + 240)
            $column++
        }
    }
    $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Preview: $Preview"
}
