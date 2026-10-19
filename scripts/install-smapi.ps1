# 安装 SMAPI 到 Stardew Valley 游戏目录（可重复执行、幂等）。
#
# 用途：仅用于本机构建引用与启动测试。SMAPI 官方推荐使用安装器；
# 本脚本复刻安装器的"手动安装"步骤（见 SMAPI 发布包 README.txt），
# 便于在无人值守环境下复现同一状态。
#
# 用法：
#   pwsh -File scripts/install-smapi.ps1 -GamePath "D:\Steam\steamapps\common\Stardew Valley"
#   pwsh -File scripts/install-smapi.ps1 -GamePath "..." -PayloadPath ".tools/smapi-payload"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GamePath,
    [string]$PayloadPath,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $PayloadPath) { $PayloadPath = Join-Path $repoRoot '.tools/smapi-payload' }
$PayloadPath = (Resolve-Path $PayloadPath).Path
$GamePath = (Resolve-Path $GamePath).Path

if (-not (Test-Path (Join-Path $GamePath 'Stardew Valley.exe'))) {
    throw "不是 Stardew Valley 游戏目录（缺少 Stardew Valley.exe）：$GamePath"
}
if (-not (Test-Path (Join-Path $PayloadPath 'StardewModdingAPI.exe'))) {
    throw "SMAPI 载荷目录无效（缺少 StardewModdingAPI.exe）：$PayloadPath"
}

$version = (Get-Item (Join-Path $PayloadPath 'StardewModdingAPI.exe')).VersionInfo.ProductVersion
Write-Host "SMAPI 载荷版本: $version"
Write-Host "目标游戏目录: $GamePath"
Write-Host "游戏版本: $((Get-Item (Join-Path $GamePath 'Stardew Valley.dll')).VersionInfo.ProductVersion)"

# 1) 拷贝 SMAPI 运行时文件（含 smapi-internal、Mods 内置包）
$items = @('StardewModdingAPI.exe', 'StardewModdingAPI.dll', 'StardewModdingAPI.exe.config',
    'StardewModdingAPI.runtimeconfig.json', 'StardewModdingAPI.xml', 'steam_appid.txt')
foreach ($i in $items) {
    $src = Join-Path $PayloadPath $i
    if (Test-Path $src) {
        if ($DryRun) { Write-Host "[dry] copy $i" } else { Copy-Item $src (Join-Path $GamePath $i) -Force }
    }
}
foreach ($d in @('smapi-internal')) {
    $src = Join-Path $PayloadPath $d
    if (Test-Path $src) {
        if ($DryRun) { Write-Host "[dry] copy dir $d" }
        else {
            $dst = Join-Path $GamePath $d
            if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst | Out-Null }
            Copy-Item (Join-Path $src '*') $dst -Recurse -Force
        }
    }
}
# SMAPI 自带的最小 Mods（ConsoleCommands / SaveBackup）：只补缺失的，不覆盖用户已有目录
$srcMods = Join-Path $PayloadPath 'Mods'
if (Test-Path $srcMods) {
    $dstMods = Join-Path $GamePath 'Mods'
    if (-not (Test-Path $dstMods)) { if (-not $DryRun) { New-Item -ItemType Directory -Path $dstMods | Out-Null } }
    foreach ($m in (Get-ChildItem $srcMods -Directory)) {
        $target = Join-Path $dstMods $m.Name
        if (-not (Test-Path $target)) {
            if ($DryRun) { Write-Host "[dry] copy mod $($m.Name)" }
            else { Copy-Item $m.FullName $target -Recurse -Force }
        }
    }
}

# 2) StardewModdingAPI.deps.json 由游戏的 deps.json 复制而来（安装器同步骤）
$gameDeps = Join-Path $GamePath 'Stardew Valley.deps.json'
$smapiDeps = Join-Path $GamePath 'StardewModdingAPI.deps.json'
if (-not (Test-Path $smapiDeps)) {
    if ($DryRun) { Write-Host '[dry] create StardewModdingAPI.deps.json' }
    else { Copy-Item $gameDeps $smapiDeps -Force }
}

# 3) 校验
if (-not $DryRun) {
    $ok = (Test-Path (Join-Path $GamePath 'StardewModdingAPI.exe')) -and
          (Test-Path $smapiDeps) -and
          (Test-Path (Join-Path $GamePath 'smapi-internal/0Harmony.dll')) -and
          (Test-Path (Join-Path $GamePath 'smapi-internal/TMXTile.dll'))
    if (-not $ok) { throw 'SMAPI 安装校验失败' }
    Write-Host 'SMAPI 安装完成并通过校验。'
}
