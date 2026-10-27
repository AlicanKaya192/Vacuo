# Draws Vacuo's own icon, the black hole of the panel (Visuals/BlackHoleScene) and of the notice (NoticeHole),
# and writes it to src\Vacuo.App\Assets\vacuo.ico with a frame for every size Windows asks for, plus logo.png
# for the panel's title. The tray, the setup and the .exe files use the .ico. The panel draws its black hole as points; at 16 px points
# would vanish, so here the disk, the bent far side and the photon ring are drawn as strokes, four times
# larger, then scaled down; the smaller the icon, the bolder the strokes.
#
#   powershell -NoProfile -File tools\make-app-icon.ps1 [-Preview <path to a .png sheet>]

param(
    [string]$Preview = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class HoleIcon
{
    // More face-on than the panel's 0.22, so the icon fills its square.
    const float Tilt = 0.34f;
    const int Supersample = 4;

    // The VOID palette (App.xaml).
    static readonly Color Void = Color.FromArgb(10, 8, 17);
    static readonly Color Accent = Color.FromArgb(157, 124, 255);
    static readonly Color AccentLight = Color.FromArgb(212, 198, 255);
    static readonly Color Hot = Color.FromArgb(244, 238, 255);
    static readonly Color Deep = Color.FromArgb(91, 63, 196);

    public static Bitmap Draw(int size)
    {
        int big = size * Supersample;
        using (var canvas = new Bitmap(big, big, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Scene(g, big, size);
            }

            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(canvas, new Rectangle(0, 0, size, size), 0, 0, big, big, GraphicsUnit.Pixel);
            }

            return bitmap;
        }
    }

    static void Scene(Graphics g, int big, int size)
    {
        float c = big / 2f;
        float radius = big * 0.47f;
        float shadow = big * (size <= 24 ? 0.26f : size <= 48 ? 0.22f : 0.19f);
        // How much bolder than at 256 px a stroke is, so a 16 px icon still shows the disk and the ring.
        float bold = size <= 16 ? 3.2f : size <= 24 ? 2.4f : size <= 40 ? 1.6f : size <= 64 ? 1.2f : 1f;
        float unit = big / 256f;

        // A soft violet halo around the shadow, fading out smoothly.
        using (var halo = new GraphicsPath())
        {
            float r = shadow * 2.3f;
            halo.AddEllipse(c - r, c - r, 2 * r, 2 * r);
            using (var brush = new PathGradientBrush(halo))
            {
                brush.CenterColor = Color.FromArgb(size <= 24 ? 120 : 90, Accent);
                brush.SurroundColors = new[] { Color.FromArgb(0, Accent) };
                brush.FocusScales = new PointF(0.42f, 0.42f);
                g.FillPath(brush, halo);
            }
        }

        Disk(g, c, radius, shadow, unit, bold, true);
        Lens(g, c, shadow, unit, bold);
        using (var brush = new SolidBrush(Void))
        {
            g.FillEllipse(brush, c - shadow, c - shadow, 2 * shadow, 2 * shadow);
        }

        PhotonRing(g, c, shadow, unit, bold);
        Disk(g, c, radius, shadow, unit, bold, false);
    }

    // The tilted gas disk as rings of short strokes: hotter and brighter inside, brighter on the left,
    // which turns toward the viewer. The far half goes behind the shadow, the near half in front of it.
    static void Disk(Graphics g, float c, float radius, float shadow, float unit, float bold, bool farSide)
    {
        // Many overlapping rings make one glowing band rather than separate lines.
        const int Rows = 30;
        const int Segments = 160;
        float inner = shadow * 1.3f;
        float gap = (radius - inner) / (Rows - 1);
        for (int row = Rows - 1; row >= 0; row--)
        {
            float t = row / (float)(Rows - 1);
            float ringRadius = inner + (radius - inner) * t;
            float fade = 0.08f + 0.5f * (1 - t) * (1 - t);
            Color color = t < 0.4f ? Blend(Hot, AccentLight, t / 0.4f) : Blend(AccentLight, Deep, (t - 0.4f) / 0.6f);
            float width = Math.Max(gap * 1.8f, (2.6f - 1.4f * t) * unit * bold);
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * 2 * (float)Math.PI / Segments;
                float a1 = (i + 1) * 2 * (float)Math.PI / Segments;
                float mid = (a0 + a1) / 2;
                // Screen y grows downward: the upper half is the far side.
                if ((Math.Sin(mid) < 0) != farSide)
                {
                    continue;
                }

                float doppler = 0.25f + 0.75f * (1 - (float)Math.Cos(mid)) / 2;
                int alpha = (int)(255 * doppler * fade);
                Arc(g, Color.FromArgb(Math.Min(255, alpha), color), width, c, ringRadius, ringRadius * Tilt, a0, a1);
            }
        }
    }

    // The far side of the disk, bent by gravity into an arc over the top of the shadow (and faintly under it).
    static void Lens(Graphics g, float c, float shadow, float unit, float bold)
    {
        const int Segments = 120;
        for (int layer = 5; layer >= 0; layer--)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * 2 * (float)Math.PI / Segments;
                float a1 = (i + 1) * 2 * (float)Math.PI / Segments;
                float mid = (a0 + a1) / 2;
                float sin = (float)Math.Sin(mid);
                bool above = sin < 0;
                float strength = above ? (float)Math.Pow(-sin, 1.1) : 0.3f * sin * sin;
                float doppler = 0.45f + 0.55f * (1 - (float)Math.Cos(mid)) / 2;
                float r = shadow * (1.08f + layer * (above ? 0.075f : 0.04f));
                int alpha = (int)(230 * strength * doppler * (1 - layer * 0.15f));
                Color color = layer < 2 ? Hot : Blend(AccentLight, Accent, (layer - 2) / 3f);
                Arc(g, Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color), Math.Max(3.2f * unit * bold, shadow * 0.09f), c, r, r, a0, a1);
            }
        }
    }

    // A piece of an ellipse centred on (c, c), flat-ended so neighbouring pieces meet without overlapping.
    static void Arc(Graphics g, Color color, float width, float c, float rx, float ry, float from, float to)
    {
        if (color.A == 0)
        {
            return;
        }

        using (var pen = new Pen(color, width))
        {
            g.DrawArc(pen, c - rx, c - ry, 2 * rx, 2 * ry, (float)(from * 180 / Math.PI), (float)((to - from) * 180 / Math.PI));
        }
    }

    // A thin bright ring with a fading glow on both sides.
    static void PhotonRing(Graphics g, float c, float shadow, float unit, float bold)
    {
        float r = shadow * 1.03f;
        var rings = new[] { Tuple.Create(Color.FromArgb(40, Accent), 7f), Tuple.Create(Color.FromArgb(80, AccentLight), 3.6f), Tuple.Create(Color.FromArgb(200, Hot), 1.6f) };
        foreach (var ring in rings)
        {
            using (var pen = new Pen(ring.Item1, ring.Item2 * unit * bold))
            {
                g.DrawEllipse(pen, c - r, c - r, 2 * r, 2 * r);
            }
        }
    }

    static Color Blend(Color from, Color to, float t)
    {
        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * t),
            (int)(from.G + (to.G - from.G) * t),
            (int)(from.B + (to.B - from.B) * t));
    }
}
'@

$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'src\Vacuo.App\Assets\vacuo.ico'
$logo = Join-Path $root 'src\Vacuo.App\Assets\logo.png'
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

# A 32-bit DIB frame, as the earlier vacuo.ico had: BITMAPINFOHEADER (height doubled for the mask), the
# pixels bottom-up in BGRA, then an empty AND mask (the alpha channel does the masking). The setup
# compiler and the .exe resources take it without question.
function DibBytes($bitmap) {
    $w = $bitmap.Width; $h = $bitmap.Height
    $data = $bitmap.LockBits((New-Object System.Drawing.Rectangle(0, 0, $w, $h)), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($w * $h * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bitmap.UnlockBits($data)
    $maskRow = [int][Math]::Ceiling($w / 32) * 4
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([uint32]40); $writer.Write([int32]$w); $writer.Write([int32](2 * $h))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32]($pixels.Length + $maskRow * $h)); $writer.Write([int32]0); $writer.Write([int32]0)
    $writer.Write([uint32]0); $writer.Write([uint32]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $writer.Write($pixels, $y * $w * 4, $w * 4) }
    $writer.Write((New-Object byte[] ($maskRow * $h)))
    $writer.Flush()
    $stream.ToArray()
}

# ICONDIR, one ICONDIRENTRY per frame, then the frames.
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$frames = foreach ($size in $sizes) { , (DibBytes ([HoleIcon]::Draw($size))) }
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
$offset = 6 + 16 * $sizes.Length
for ($i = 0; $i -lt $sizes.Length; $i++) {
    $edge = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
# The cast matters: PowerShell hands the frames over as object[], which Write would not take as bytes.
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($output, $stream.ToArray())
Write-Output "Icon: $output"

# The panel's title shows the logo at 22 px; 64 px covers up to 290 % display scaling.
[HoleIcon]::Draw(64).Save($logo, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Logo: $logo"

# The preview sheet: 16, 24, 32, 48 and 128 px on the dark and the light taskbar.
if ($Preview) {
    [HoleIcon]::Draw(256).Save([System.IO.Path]::ChangeExtension($Preview, '.256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet = New-Object System.Drawing.Bitmap -ArgumentList 520, 360
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 0, 520, 180)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(243, 243, 243))), 0, 180, 520, 180)
    foreach ($top in 0, 180) {
        $x = 20
        foreach ($size in 16, 24, 32, 48) {
            $g.DrawImage([HoleIcon]::Draw($size), $x, $top + 80 - $size / 2, $size, $size)
            $x += $size + 30
        }
        $g.DrawImage([HoleIcon]::Draw(128), $x + 20, $top + 26, 128, 128)
    }
    $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Preview: $Preview"
}
