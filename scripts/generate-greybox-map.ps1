# 生成主题农场灰盒地图（TMX）。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/generate-greybox-map.ps1
#
# 产物：content/[CP] LeliaFarm/maps/manor_farm.tmx
#
# 设计（详见 docs/map-contract.md）：
#   尺寸 64 x 40 格，16px 图块。
#   图层 Back / Buildings / Paths / Front（星露谷农场地图必需的四层）。
#   图块索引：firstgid=1，local 1=草地，2=土壤(Type=Dirt)，3=石板路。
#   交付入口固定在地块 (42,18)，位于石板路上。
#
# 这是灰盒：只保证结构、图层、图块属性与坐标契约正确，外观由 Codex 替换贴图后重新导出。

[CmdletBinding()]
param(
    [int]$Width = 64,
    [int]$Height = 40
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$mapPath = Join-Path $repoRoot 'content/[CP] LeliaFarm/maps/manor_farm.tmx'

# ---- 图块编号（与 tilesheet 一致）----
$GID_GRASS = 2   # firstgid(1) + local 1
$GID_DIRT = 3    # firstgid(1) + local 2
$GID_PATH = 4    # firstgid(1) + local 3

$EMPTY = 0

# ---- 区域定义 ----
$cropArea = @{ X0 = 30; Y0 = 12; X1 = 45; Y1 = 22 }      # 作物区（土壤，可耕种）
$pathRow = 18                                             # 主路（水平）
$pathCol = 22                                             # 由农舍方向通到主路的竖路
$forestX0 = 4; $forestX1 = 26; $forestY0 = 26; $forestY1 = 36   # 林地（灰盒留空，供种植树木）
$pondX0 = 50; $pondX1 = 58; $pondY0 = 26; $pondY1 = 34          # 池塘占位（灰盒只留空区）

function Get-BackTile([int]$x, [int]$y) {
    if ($x -ge $cropArea.X0 -and $x -le $cropArea.X1 -and $y -ge $cropArea.Y0 -and $y -le $cropArea.Y1) {
        return $GID_DIRT
    }
    return $GID_GRASS
}

function Get-PathTile([int]$x, [int]$y) {
    if ($y -eq $pathRow -and $x -ge 8 -and $x -le 56) { return $GID_PATH }
    if ($x -eq $pathCol -and $y -ge 6 -and $y -le $pathRow) { return $GID_PATH }
    return $EMPTY
}

# ---- 生成各图层 CSV ----
function New-LayerData([scriptblock]$resolver) {
    $rows = New-Object System.Collections.Generic.List[string]
    for ($y = 0; $y -lt $Height; $y++) {
        $cells = New-Object System.Collections.Generic.List[string]
        for ($x = 0; $x -lt $Width; $x++) {
            $cells.Add([string](& $resolver $x $y))
        }
        $rows.Add(($cells -join ','))
    }
    return ($rows -join "`n")
}

Write-Host "生成图层数据 ($Width x $Height) ..."
$backData = New-LayerData { param($x, $y) Get-BackTile $x $y }
$pathData = New-LayerData { param($x, $y) Get-PathTile $x $y }
$emptyData = New-LayerData { param($x, $y) $EMPTY }

$tilesetImage = 'Maps/lelia_farm_tiles'

$map = @"
<?xml version="1.0" encoding="UTF-8"?>
<map version="1.10" tiledversion="1.10.2" orientation="orthogonal" renderorder="right-down" width="$Width" height="$Height" tilewidth="16" tileheight="16" infinite="0" nextlayerid="6" nextobjectid="1">
 <properties>
  <property name="Outdoors" value="T"/>
  <property name="LeliaFarmMap" value="1"/>
 </properties>
 <tileset firstgid="1" name="lelia_farm_tiles" tilewidth="16" tileheight="16" tilecount="4" columns="4">
  <image source="$tilesetImage" width="64" height="16"/>
  <tile id="2">
   <properties>
    <property name="Type" value="Dirt"/>
   </properties>
  </tile>
  <tile id="3">
   <properties>
    <property name="Type" value="Stone"/>
   </properties>
  </tile>
 </tileset>
 <layer id="1" name="Back" width="$Width" height="$Height">
  <data encoding="csv">
$backData
</data>
 </layer>
 <layer id="2" name="Buildings" width="$Width" height="$Height">
  <data encoding="csv">
$emptyData
</data>
 </layer>
 <layer id="3" name="Paths" width="$Width" height="$Height">
  <data encoding="csv">
$pathData
</data>
 </layer>
 <layer id="4" name="Front" width="$Width" height="$Height">
  <data encoding="csv">
$emptyData
</data>
 </layer>
</map>
"@

$dir = Split-Path -Parent $mapPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllText($mapPath, $map, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  写入 $mapPath"
Write-Host "灰盒地图生成完成。交付入口 = (42,18)；作物区 = x 30..45, y 12..22。"
