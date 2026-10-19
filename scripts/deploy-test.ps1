# 把构建产物部署到明确的测试目录。
#
# 只清理并写入本项目自己的两个文件夹（LeliaFarm.Core / [CP] LeliaFarm），
# 不触碰 Mods 下的其他 Mod，也不删除用户存档。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy-test.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy-test.ps1 -ModsDir "D:\TestMods" -Configuration Release
#
# 前置：先 dotnet build src/LeliaFarm.Core/LeliaFarm.Core.csproj

[CmdletBinding()]
param(
    [string]$ModsDir,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# 读取本机游戏路径（local.props），仅在未显式指定 -ModsDir 时使用。
if (-not $ModsDir) {
    $localProps = Join-Path $repoRoot 'local.props'
    $gamePath = $env:LELIAFARM_GAMEPATH
    if (-not $gamePath -and (Test-Path $localProps)) {
        # 用正则取第一个非空的 <GamePath>，避免 [xml] 转换把元素转成 XmlElement 字符串。
        $match = Select-String -LiteralPath $localProps -Pattern '<GamePath[^>]*>([A-Za-z]:\\[^<]+)</GamePath>' |
            Where-Object { $_.Matches[0].Groups[1].Value.Trim() } | Select-Object -First 1
        if ($match) { $gamePath = $match.Matches[0].Groups[1].Value.Trim() }
    }
    if (-not $gamePath) { throw "无法确定游戏路径：请指定 -ModsDir，或设置 LELIAFARM_GAMEPATH。" }
    $ModsDir = Join-Path $gamePath 'Mods'
}

$coreSource = Join-Path $repoRoot "src/LeliaFarm.Core/bin/$Configuration/net6.0"
$packSource = Join-Path $repoRoot 'content/[CP] LeliaFarm'

if (-not (Test-Path -LiteralPath (Join-Path $coreSource 'LeliaFarm.Core.dll'))) {
    throw "缺少构建产物：$coreSource\LeliaFarm.Core.dll（先执行 dotnet build）"
}
if (-not (Test-Path -LiteralPath (Join-Path $packSource 'manifest.json'))) {
    throw "缺少内容包：$packSource\manifest.json"
}
if (-not (Test-Path -LiteralPath $ModsDir)) { New-Item -ItemType Directory -Path $ModsDir -Force | Out-Null }

$targets = @(
    @{ Name = 'LeliaFarm.Core'; Source = $coreSource; Filter = @('LeliaFarm.Core.dll', 'manifest.json', 'i18n', 'assets') },
    @{ Name = '[CP] LeliaFarm'; Source = $packSource; Filter = @('manifest.json', 'content.json', 'maps', 'assets', 'i18n') }
)

foreach ($t in $targets) {
    $dest = Join-Path $ModsDir $t.Name
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null

    foreach ($item in $t.Filter) {
        $src = Join-Path $t.Source $item
        if (-not (Test-Path -LiteralPath $src)) { continue }
        Copy-Item -LiteralPath $src -Destination (Join-Path $dest $item) -Recurse -Force
    }

    $files = (Get-ChildItem -LiteralPath $dest -Recurse -File | Measure-Object).Count
    Write-Host "已部署 $($t.Name) -> $dest（$files 个文件）"
}

Write-Host ''
Write-Host '部署完成。测试目录：' $ModsDir
Write-Host '启动方式：运行游戏目录下的 StardewModdingAPI.exe，日志见 %APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt'
