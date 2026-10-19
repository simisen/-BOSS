# 生成开发用占位素材（PNG）。产物全部标记为占位，不能当作完成品发布。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/generate-placeholder-art.ps1
#
# 说明：这里用 System.Drawing 生成纯色/软件占位图，只为了让"HUD 立绘、雾、图块"有可加载的
# 真实文件，从而在游戏里跑通加载与降级路径。正式素材由 Codex 提供并替换同名文件。

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing

function New-SolidImage {
    param([string]$Path, [int]$Width, [int]$Height, [System.Drawing.Color]$Color)
    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($Color)
    $g.Dispose()
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  写入 $Path ($Width x $Height)"
}

function New-PortraitPlaceholder {
    param([string]$Path, [System.Drawing.Color]$Accent, [string]$Label)
    $w = 256; $h = 384
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None

    # 身体剪影（灰盒）
    $bodyBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 58, 62, 58))
    $g.FillRectangle($bodyBrush, 48, 140, 160, 244)
    # 头部
    $headBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 78, 82, 76))
    $g.FillEllipse($headBrush, 78, 40, 100, 120)
    # 主题强调色（灰绿/暖褐）
    $accentBrush = New-Object System.Drawing.SolidBrush($Accent)
    $g.FillRectangle($accentBrush, 48, 140, 160, 14)
    $g.FillRectangle($accentBrush, 48, 336, 160, 8)

    $font = New-Object System.Drawing.Font("Consolas", 13)
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 210, 214, 206))
    $g.DrawString("PLACEHOLDER", $font, $textBrush, 62, 200)
    $g.DrawString($Label, $font, $textBrush, 62, 222)

    $g.Dispose()
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  写入 $Path ($w x $h, 占位立绘)"
}

function New-FogPlaceholder {
    param([string]$Path)
    $size = 256
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)

    # 软边云雾：若干半透明同心椭圆叠加，四边自然淡出，便于无缝平铺观感。
    for ($i = 0; $i -lt 5; $i++) {
        $alpha = 18 - ($i * 3)
        $inset = 18 + ($i * 22)
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($alpha, 226, 230, 226))
        $g.FillEllipse($brush, $inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset))
        $brush.Dispose()
    }

    $g.Dispose()
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  写入 $Path ($size x $size, 占位雾)"
}

Write-Host "生成 Core 占位素材 ..."
$core = Join-Path $repoRoot 'src/LeliaFarm.Core/assets'
New-SolidImage -Path (Join-Path $core 'ui/hud_panel.png') -Width 1 -Height 1 -Color ([System.Drawing.Color]::White)
New-PortraitPlaceholder -Path (Join-Path $core 'portraits/lelia_neutral.png') -Accent ([System.Drawing.Color]::FromArgb(255, 96, 110, 96)) -Label 'neutral'
New-PortraitPlaceholder -Path (Join-Path $core 'portraits/lelia_working.png') -Accent ([System.Drawing.Color]::FromArgb(255, 112, 104, 84)) -Label 'working'
New-PortraitPlaceholder -Path (Join-Path $core 'portraits/lelia_tired.png') -Accent ([System.Drawing.Color]::FromArgb(255, 84, 84, 92)) -Label 'tired'
New-FogPlaceholder -Path (Join-Path $core 'effects/fog_soft.png')

Write-Host "生成内容包图块贴图 ..."
$cp = Join-Path $repoRoot 'content/[CP] LeliaFarm/assets/tilesheets'
$tile = 16
$count = 4
$sheet = New-Object System.Drawing.Bitmap(($tile * $count), $tile)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::Transparent)

# 0: 透明（保留不用）
# 1: 草地（低饱和灰绿）
$g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 74, 87, 72))), $tile, 0, $tile, $tile)
# 2: 土壤（灰褐，用于耕种区；TMX 里带 Type=Dirt 属性）
$g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 92, 83, 72))), ($tile * 2), 0, $tile, $tile)
# 3: 石板路（湿石灰）
$g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 110, 106, 98))), ($tile * 3), 0, $tile, $tile)

# 轻微网格线，便于确认图块边界（正式素材替换时去掉）
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(40, 0, 0, 0))
for ($i = 1; $i -lt $count; $i++) { $g.DrawLine($pen, ($i * $tile), 0, ($i * $tile), $tile) }
$g.Dispose()

if (-not (Test-Path $cp)) { New-Item -ItemType Directory -Path $cp -Force | Out-Null }
$sheetPath = Join-Path $cp 'lelia_farm_tiles.png'
$sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
Write-Host "  写入 $sheetPath ($($tile * $count) x $tile, 4 图块)"

# 农场选择界面图标（占位）：灰绿底 + 修道院尖顶剪影
$iconSize = 32
$icon = New-Object System.Drawing.Bitmap($iconSize, $iconSize)
$g2 = [System.Drawing.Graphics]::FromImage($icon)
$g2.Clear([System.Drawing.Color]::FromArgb(255, 58, 66, 56))
$g2.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 92, 83, 72))), 6, 20, 20, 8)
$g2.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 196, 210, 180))), @(
  (New-Object System.Drawing.Point(16, 4)),
  (New-Object System.Drawing.Point(24, 20)),
  (New-Object System.Drawing.Point(8, 20))
))
$g2.Dispose()
$iconDir = Join-Path $repoRoot 'content/[CP] LeliaFarm/assets/ui'
if (-not (Test-Path $iconDir)) { New-Item -ItemType Directory -Path $iconDir -Force | Out-Null }
$iconPath = Join-Path $iconDir 'farm_icon.png'
$icon.Save($iconPath, [System.Drawing.Imaging.ImageFormat]::Png)
$icon.Dispose()
Write-Host "  写入 $iconPath ($iconSize x $iconSize, 农场图标占位)"

Write-Host "占位素材生成完成。"
