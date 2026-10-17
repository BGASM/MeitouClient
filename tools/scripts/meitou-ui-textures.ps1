# Crops the Meitou UI kit's textures from the game's Kenshi_UI.png into docs/meitou-ui/reference/textures/
# (git-ignored). Rectangles are atlas pixels from data/gui/skins/kenshi_skins.xml; see docs/meitou-ui.md.
# Usage: powershell -File tools/scripts/meitou-ui-textures.ps1   (install from KENSHI_PATH or meitou.local.json)
param([string]$Kenshi = $env:KENSHI_PATH)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $Kenshi) {
  $cfg = Join-Path $repo 'meitou.local.json'
  if (Test-Path $cfg) { $Kenshi = (Get-Content -Raw $cfg | ConvertFrom-Json).kenshiPath }
}
$atlasPath = Join-Path "$Kenshi" 'data\gui\gfx\Kenshi_UI.png'
if (-not $Kenshi -or -not (Test-Path $atlasPath)) { throw "Kenshi_UI.png not found. Set KENSHI_PATH or kenshiPath in meitou.local.json." }

$out = Join-Path $repo 'docs\meitou-ui\reference\textures'
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName System.Drawing
$atlas = [System.Drawing.Bitmap]::FromFile($atlasPath)

# name = x, y, w, h
$rects = [ordered]@{
  'paper-inner'     = 346, 451, 176, 93    # GenericTextBoxSkin (341,446,186,102) minus its soft alpha rim
  'rust'            = 1342, 120, 492, 546
  'button'          = 612, 766, 118, 45    # Button1Skin normal
  'button-pressed'  = 741, 766, 118, 45    # Button1Skin pushed
  'message'         = 761, 251, 170, 70    # MessageSkin
  'close'           = 697, 542, 103, 32    # CloseButtonSkin normal / highlighted / pushed at x 0 / 36 / 72
  'speed'           = 879, 550, 186, 211   # TimeSpeedButton1-4Skin
  'tick-off'        = 601, 456, 35, 33     # TickBoxSkin
  'tick-on'         = 639, 456, 35, 33
  'slot'            = 826, 583, 25, 25     # InventorySlotSkin
  'selection'       = 724, 298, 25, 23     # SelectionPanel
  'bar-life'        = 338, 640, 300, 26    # LifeBarSkin
  'bar-healed'      = 338, 614, 300, 26    # LifeBarHealedSkin
  'bar-stunned'     = 338, 666, 300, 26    # LifeBarStunnedSkin
  'bar-hurt'        = 338, 692, 300, 26    # LifeBarHurtSkin
  'bar-robot'       = 338, 148, 300, 26    # LifeBarRobotSkin
  'progress-empty'  = 900, 479, 140, 16    # ProgressBarEmptySkin
  'progress-filled' = 900, 458, 140, 16    # ProgressBarFilledSkin
  'order-icons'     = 0, 0, 256, 170       # order-icon sprite
  'icon-continue'   = 166, 0, 30, 40
  'icon-new'        = 108, 90, 33, 34
  'icon-load'       = 222, 90, 32, 34
  'icon-options'    = 146, 90, 34, 34
  'icon-credits'    = 49, 0, 44, 36
  'icon-exit'       = 202, 126, 30, 35
}
foreach ($name in $rects.Keys) {
  $r = $rects[$name]
  $rect = New-Object System.Drawing.Rectangle($r[0], $r[1], $r[2], $r[3])
  $crop = $atlas.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $crop.Save((Join-Path $out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $crop.Dispose()
}
$atlas.Dispose()
"$($rects.Count) textures in $out"
