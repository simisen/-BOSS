# 环境与版本冻结（M0）

本文记录**本机实测**的环境事实、检查方法、构建命令与缺失项。
私人绝对路径只出现在本地配置（`local.props`）与本文的"本机值"一栏；对外说明使用占位路径。

记录日期：2026-10-19。

## 1. 版本冻结表

| 组件 | 版本（实测） | 检查方法 | 结论 |
|---|---|---|---|
| 操作系统 | Windows 11 `10.0.26100.0` | `[System.Environment]::OSVersion` | 可用 |
| Stardew Valley | **1.6.15**，FileVersion `1.6.15.24356`，Steam buildid `16826371` | `(Get-Item 'Stardew Valley.dll').VersionInfo.FileVersion`；`appmanifest_413150.acf` 的 `buildid` | 可用 |
| 游戏运行时 | **net6.0**（`Stardew Valley.runtimeconfig.json` → `"tfm": "net6.0"`，includedFrameworks `Microsoft.NETCore.App 6.0.32`） | 读取 runtimeconfig | 决定 Mod 必须 `TargetFramework=net6.0` |
| SMAPI | **4.5.2**（`4.5.2+821167e5c511bf3a2d98f604e5e838561c469219`），要求游戏 ≥ 1.6.14 | `StardewModdingAPI.exe` 的 ProductVersion | 可用，已加载 |
| .NET SDK | **10.0.400** | `dotnet --info` | 可构建 net6.0 目标 |
| .NET 运行时 | 3.1.10 / 6.0.27 / 6.0.33 / 8.0.13 / 10.0.11 | `dotnet --list-runtimes` | 含 6.0.33（游戏用 6.0） |
| `Pathoschild.Stardew.ModBuildConfig` | **4.4.0**（NuGet，固定版本） | `PackageReference` | 可用，构建不自动部署 |
| Content Patcher | **未安装（缺失）** | `Mods` 目录不存在 CP；GitHub 无 CP 发布资产 | **阻塞项**，见 §5 |

游戏安装目录（本机值）：`F:\steam\steamapps\common\Stardew Valley`
对外占位路径：`<STARDEW_GAME_DIR>`、`<STARDEW_MODS_DIR>`

## 2. 已核实的游戏 API（反射实测，非网络示例）

用 `tools` 脚本（`.tools/apidump`，见 §4）直接反射 `Stardew Valley.dll` / `StardewValley.GameData.dll`：

| 事实 | 证据 |
|---|---|
| `Data/AdditionalFarms` 的资产名 | 游戏内容目录存在 `Content\Data\AdditionalFarms.xnb` |
| 其数据类型为 `Dictionary<string, ModFarmType>` | 反射 `StardewValley.GameData.ModFarmType` 字段：`Id` / `TooltipStringPath` / `MapName` / `IconTexture` / `WorldMapTexture` / `SpawnMonstersByDefault` / `ModData` / `CustomFields` |
| 农场类型判定入口 | `StardewValley.Game1.GetFarmTypeID()`（静态，返回 `string`） |
| 动物抚摸状态 | `StardewValley.FarmAnimal.wasPet`（`NetBool`）、`pet(Farmer, bool)`、`.ctor(string type, long id, long ownerID)` |
| 取农场全部动物 | `GameLocation.getAllFarmAnimals()` |
| 添加动物 | `Utility.addAnimalToFarm(FarmAnimal)` → `bool` |
| 作物/耕地 | `Crop.dead`（`NetBool`）、`Crop.ctor(string seedId, int tileX, int tileY, GameLocation)`、`HoeDirt.ctor(int startingState, GameLocation)` |
| 建筑 | `Building.ctor(string type, Vector2 tile)`、`Building.daysOfConstructionLeft`（`NetInt`） |
| 体力 | `Farmer.Stamina`（`float`）/ `MaxStamina`（`int`） |
| HUD 工具条位置 | `StardewValley.Menus.Toolbar.yPositionOnScreen`；`Game1.onScreenMenus`（`IList<IClickableMenu>`） |
| HUD 消息 | `Game1.addHUDMessage(HUDMessage)` |
| 模组内容管理器支持的扩展名 | SMAPI 内部字符串：`fnt` / `png` / `tbin` / `tmx` → **SMAPI 可直接加载 `.tmx`/`.tbin` 地图** |
| SMAPI 内部注册的 TMX 支持 | SMAPI 4.5.2 自带 `smapi-internal/TMXTile.dll` |

**未核实**（需要实机或 Tiled 才能确认）：自定义农场类型下农舍/出货箱/邮箱的自动放置；地图 `Warp` 出口坐标；
TMX 里 `<image source="Maps/...">` 是否被内容管线当作**资产名**解析。见 `docs/map-contract.md` 的"未验证项"。

## 3. 构建

```powershell
# 还原 + 构建（不自动部署、不打包 zip）
dotnet build src/LeliaFarm.Core/LeliaFarm.Core.csproj

# 产物
# src/LeliaFarm.Core/bin/Debug/net6.0/  → LeliaFarm.Core.dll / manifest.json / assets/ / i18n/
```

关键 MSBuild 设置（`src/LeliaFarm.Core/LeliaFarm.Core.csproj`）：

| 设置 | 值 | 原因 |
|---|---|---|
| `TargetFramework` | `net6.0` | 与游戏 `runtimeconfig.json` 一致；SMAPI 4.x 运行在 net6.0 |
| `EnableModDeploy` | `false` | 构建时**不**写入玩家 `Mods` 目录 |
| `EnableModZip` | `false` | 不自动产出 zip |
| `GamePath` | 来自 `local.props`（私有，未纳入发布）或环境变量 `LELIAFARM_GAMEPATH` | 私人路径不写进公开文件 |

`local.props` 内容模板（占位）：

```xml
<Project>
  <PropertyGroup>
    <GamePath Condition="'$(GamePath)' == '' AND '$(LELIAFARM_GAMEPATH)' != ''">$(LELIAFARM_GAMEPATH)</GamePath>
    <GamePath Condition="'$(GamePath)' == ''">&lt;STARDEW_GAME_DIR&gt;</GamePath>
  </PropertyGroup>
</Project>
```

## 4. 环境搭建与检查脚本

| 脚本 | 作用 | 是否幂等 |
|---|---|---|
| `scripts/install-smapi.ps1` | 把 SMAPI 发布包的手动安装步骤落地到游戏目录（`StardewModdingAPI.exe`、`smapi-internal/`、`StardewModdingAPI.deps.json`、内置 Mods） | 是（已存在的目录/文件不覆盖） |
| `scripts/deploy-test.ps1` | 把构建产物部署到明确的测试目录（默认 `<游戏目录>\Mods`），只清理本项目两个文件夹 | 是 |
| `scripts/generate-placeholder-art.ps1` | 生成占位 PNG（HUD 底色、三张立绘、雾、图块、农场图标） | 是（覆盖同名文件） |
| `scripts/generate-greybox-map.ps1` | 生成灰盒 TMX 地图 | 是 |

SMAPI 载荷来源：`SMAPI-4.5.2-installer.zip` → `internal/windows/install.dat`（zip 改名）→ 解包为 `.tools/smapi-payload`。
这**不是**伪造安装：执行的就是 SMAPI 官方 README 描述的 manual install 步骤。

`dotnet` 在这个沙箱里不能在 `pwsh` 管道里直接取 stdout（命名管道被限制）。
检查版本的可行方式：

```powershell
& dotnet --info *> "$env:TEMP\dotnet_info.txt" 2>&1; Get-Content "$env:TEMP\dotnet_info.txt"
```

## 5. 缺失项与阻塞（真实状态）

| 缺失/阻塞 | 影响 | 已做的替代验证 |
|---|---|---|
| **Content Patcher 未安装** | 内容包 `[CP] LeliaFarm` 无法加载 → 主题农场类型不会被注册 → **农场相关实机检查全部 Not Run** | 内容包 JSON 结构校验、TMX 结构校验（`MapAndContentPackTests`）；Core 在缺内容包时安全闲置的路径已实机验证 |
| CP 只在 Nexus 分发，无 GitHub 发布资产 | 无法在无人值守环境自动安装 | 记录在案；`content.json` 使用 `Format: 2.0.0` 以兼容较旧 CP |
| 无现成存档，且无法非交互创建存档 | 睡觉/读档/切换存档、HUD 实际外观、雾效、任务进度等**实机检查 Not Run** | 存档读写与迁移逻辑用 49 项单元测试覆盖（含高版本拒绝写入、损坏数据保留） |
| 无法显示游戏画面做像素级确认 | 立绘位置、任务栏避让、地图观感只有坐标契约，没有截图 | HUD 布局用纯函数 + 三档分辨率的单元测试断言"在视口内、避开工具栏与右上保留区、互不重叠" |

## 6. 日志位置（实机检查用）

- SMAPI 日志：`%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt`（UTF-8）
- 启动方式：直接运行 `<游戏目录>\StardewModdingAPI.exe`（本环境不走 Steam 启动项）
- 实测：本机在 2560×1440 下 10 秒内到达标题画面，`Lelia Farm Core 0.1.0 by AliceXitina` 正常加载，无异常。

## 7. 版本升级时的复核清单

1. 重新读 `Stardew Valley.runtimeconfig.json` 的 `tfm` 与 `Stardew Valley.deps.json` 的 `runtimeTarget`。
2. 重新反射本文 §2 表格里每个入口，确认签名未变（`.tools/apidump`）。
3. 确认 `Data/AdditionalFarms.xnb` 仍存在且 `ModFarmType` 字段未变。
4. 更新 `Pathoschild.Stardew.ModBuildConfig` 到与 SMAPI 匹配的版本，**固定**具体版本。
5. 重跑 `dotnet test`，再按 `docs/testing.md` 的实机清单逐项验证。
6. 更新本文表格与 `docs/CHANGELOG.md`。
