# Renders Kenshi message-banner labels (MessageSkin from Kenshi_UI.png, Exo 2 SemiBold) as transparent PNGs.
# Usage: .\make-labels.ps1 [-Labels "Vanilla","Faithful","Meitou"] [-Scale 1.5] [-Width 480] [-Align Left] [-Padding 40] [-HeightScale 1] [-Cap 1] [-Out .\labels]
param(
  [string[]]$Labels = @('Vanilla', 'Faithful', 'Meitou'),
  [double]$Scale = 1.5,
  [int]$Width = 480,
  [string]$Out = (Join-Path $PSScriptRoot 'labels'),
  [string]$Game = 'E:\Kenshi_RE\data\gui',
  [ValidateSet('Center', 'Left')][string]$Align = 'Center',
  [double]$Padding = 40,      # space beside the text, in source pixels (x Scale)
  [double]$HeightScale = 1,  # squash or stretch the banner vertically
  [double]$Cap = 1           # squash the worn end caps horizontally
)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null

$atlas = [System.Drawing.Bitmap]::FromFile("$Game\gfx\Kenshi_UI.png")
$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$fonts.AddFontFile("$Game\fonts\Exo2-SemiBold.ttf")
$family = $fonts.Families[0]

# MessageSkin: source 761,251 170x70; left cap 82, stretched middle 35, right cap 53.
$sx = 761; $sy = 251; $sh = 70; $L = 82; $R = 53; $M = 35
$pad = 6                                   # transparent margin so the worn edges never clip
$H = [int][math]::Round($sh * $Scale * $HeightScale)
$cl = [int][math]::Round($L * $Scale * $Cap); $cr = [int][math]::Round($R * $Scale * $Cap)
$W = [math]::Max($Width, $cl + $cr + 1)
# Widen every banner in the set to fit the longest label, so they all match.
$probe = New-Object System.Drawing.Bitmap(1, 1); $pg = [System.Drawing.Graphics]::FromImage($probe)
$pf = New-Object System.Drawing.Font($family, [single](22 * $Scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
foreach ($label in $Labels) { $W = [math]::Max($W, [int]($pg.MeasureString($label, $pf).Width + 2 * $Padding * $Scale)) }
$pg.Dispose(); $probe.Dispose(); $pf.Dispose()

$attr = New-Object System.Drawing.Imaging.ImageAttributes
$attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # no edge bleed when stretching
function Draw-Part($g, $dx, $dw, $srcX, $srcW) {
  $dest = New-Object System.Drawing.Rectangle(($pad + $dx), $pad, $dw, $H)
  $g.DrawImage($atlas, $dest, $srcX, $sy, $srcW, $sh, [System.Drawing.GraphicsUnit]::Pixel, $attr)
}

foreach ($label in $Labels) {
  $bmp = New-Object System.Drawing.Bitmap(($W + 2 * $pad), ($H + 2 * $pad), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

  Draw-Part $g 0 $cl $sx $L
  Draw-Part $g $cl ($W - $cl - $cr) ($sx + $L) $M
  Draw-Part $g ($W - $cr) $cr ($sx + $L + $M) $R

  # Text: the game's Secondary colour on paper, Floater size (22px) scaled with the banner.
  $font = New-Object System.Drawing.Font($family, [single](22 * $Scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
  $fmt = New-Object System.Drawing.StringFormat
  $fmt.Alignment = if ($Align -eq 'Left') { [System.Drawing.StringAlignment]::Near } else { [System.Drawing.StringAlignment]::Center }
  $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
  $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#140806'))
  $inset = if ($Align -eq 'Left') { $Padding * $Scale } else { 0 }   # left-aligned text starts inside the worn edge
  $rect = New-Object System.Drawing.RectangleF([single]($pad + $inset), [single]($pad - 1 * $Scale), [single]($W - $inset), [single]$H)
  $g.DrawString($label, $font, $brush, $rect, $fmt)

  $g.Dispose(); $font.Dispose(); $brush.Dispose()
  $slug = ($label.ToLower() -replace '&', 'and' -replace '[^a-z0-9]+', '-').Trim('-')
  $file = Join-Path $Out "$slug.png"
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  "{0}  {1}x{2}" -f $file, ($W + 2 * $pad), ($H + 2 * $pad)
}
$atlas.Dispose(); $fonts.Dispose()
