# 测试与质量记录

## 1. 自动化测试（已运行）

```powershell
dotnet test tests/LeliaFarm.Tests/LeliaFarm.Tests.csproj
```

最近一次结果：**49 项通过 / 0 失败 / 0 跳过**（net8.0，xUnit 2.9.2，Microsoft.NET.Test.Sdk 17.11.1）。

测试工程**不引用 SMAPI 或游戏程序集**，通过 `<Compile Include="...">` 链接纯逻辑源码，
所以领域规则可以脱离游戏进程运行。

| 覆盖目标 | 测试文件 | 关键断言 |
|---|---|---|
| 作物数量扫描不重复累加、完成后不因收获失效 | `TaskProgressTests.cs` | 连续 10 次观测停在 5；收获后 2 格仍为 Claimed 且 Progress=5 |
| 交付不足/非法物品/重复点击/重复事件 | `TaskProgressTests.cs` | 空手→`NoItemHeld`、错物品→`ItemNotAccepted`、`busy`→`Busy`、已领→`AlreadyClaimed`；`CompleteAndClaim` 幂等、第二次不发奖励 |
| 奖励只发一次 | `TaskProgressTests.cs` | 自动结算只给一次；`autoClaim=false` 时观测**不**发奖励，`TryClaim` 才发 |
| 交付型任务不能被观测完成 | `TaskProgressTests.cs` | `ApplyObservation` 抛 `InvalidOperationException` |
| 存档重载保留、新存档为空、高版本不覆盖、迁移失败不覆盖 | `SaveMigrationTests.cs` | 空→`Empty`；同名状态往返；`schemaVersion=999`→`TooNew` 且 `CanWrite=false`、原始 JSON 原样保留；坏 JSON→`Corrupt` 且保留原文；未知任务 ID 保留 |
| 无效素材清单、越界帧、重复 ID、越界参数 | `VisualManifestTests.cs` | `..` 穿越、绝对路径、重复 ID、帧越界、未知 kind、Map 用 .png、HUD/雾参数越界、缺 provenance，全部被拒 |
| HUD 矩形在视口内、避让区域与缩放 | `HudLayoutTests.cs` | 1280×720 / 1920×1080 / 2560×1440 三档：在视口内、不压工具栏、不压右上保留区、立绘与任务栏不重叠；UserScale=2 仍在视口内；极小窗口折叠 |
| 配置驱动的任务定义 | `TaskDefinitionSetTests.cs` | 未知 objectiveType、数字型 objectiveType、重复 ID、低版本、DeliverProduce 缺物品、未知 scope 全部拒绝 |
| **实物**任务配置合法 | `TaskDefinitionSetTests.cs` | 直接解析随包 `assets/tasks.json`（不是示例） |
| **实物**视觉清单合法 | `MapAndContentPackTests.cs` | 直接解析随包 `assets/visual-manifest.json` |
| 地图结构可运行性 | `MapAndContentPackTests.cs` | 四图层 64×40、CSV 行列数、`Outdoors=T`、图块 2 带 `Type=Dirt`、tilesheet 指向内容资产名、交付点 (42,18) 在道路层 |
| 内容包引用完整 | `MapAndContentPackTests.cs` | `content.json` 每个 `FromFile` 都真实存在；`EditData` 的农场 ID 与 `MapName` 与 Core 常量一致；manifest 声明 CP 依赖 |

> 说明：`ReadSaveData/WriteSaveData` 本身是 SMAPI 调用，未在单元测试里覆盖；
> 被覆盖的是它前面/后面的纯逻辑（版本判定、迁移、只读保护、幂等）。

## 2. 实机检查（真实状态）

环境：Stardew Valley 1.6.15 + SMAPI 4.5.2，2560×1440，Windows 11。
启动方式：`StardewModdingAPI.exe`（不使用 Steam 启动项）。日志：`%APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt`。

| 检查 | 预期 | 实测结果 |
|---|---|---|
| SMAPI 启动到标题画面 | 正常 | **Pass**（约 10 s 到标题，`setGameMode('titleScreenGameMode')`） |
| Core 被加载 | 出现在 "Loaded mods" | **Pass**（`Lelia Farm Core 0.1.0 by AliceXitina`） |
| Core 入口无异常 | 无 ERROR 堆栈 | **Pass**（Entry 日志：启动行、视觉清单已加载、任务定义已加载、GameLaunched） |
| 视觉清单校验生效 | 4 个占位资源被识别并警告 | **Pass**（`有 4 个资源标记为占位`） |
| 内容包缺失时 Core 的降级路径 | 只诊断、不报错、不生成农场 | **Partial**：内容包确实被 SMAPI 跳过（缺 CP），Core 无异常；但"进入存档后闲置"需存档才能验证 → **Not Run** |
| 新建主题农场 | 主题加载、原版农场不变 | **Not Run** —— 缺 Content Patcher |
| 耕地 / 播种 / 浇水 / 砍树 / 开菜单 | 原版功能正常 | **Not Run** —— 需要存档 |
| 猪鸡与畜舍 | 可互动、可回舍、重复命令不重复生成 | **Not Run** —— 需要存档 |
| 三项目标与交付 | 进度一致、重复交付不多扣不多给 | **Not Run** —— 需要存档；逻辑层已由单元测试覆盖 |
| 睡觉 / 退出 / 读档 / 切换存档 | 状态正确隔离、无残留 | **Not Run** —— 需要存档 |
| 晴天/雨天/夜晚/四季 | 地图有效、夜间可读 | **Not Run** —— 需要存档 |
| 1280×720 / 1920×1080 / 2560×1440 | 记录世界/UI 缩放，UI 不冲突 | **Partial**：只有 `HudLayoutTests` 的纯函数断言；实际显示 **Not Run** |
| 缺可选图片 / 坏配置 / 关雾 | 可诊断且降级，玩法仍可运行 | **Partial**：清单校验与降级代码路径有单元测试；实机降级 **Not Run** |
| 重复进出农场、返回标题 | 不残留光源、不重复绑定、资源不增长 | **Not Run** |

**编译通过不等于运行验证通过。** 上表里凡是 `Not Run` 的都保持 `Not Run`，没有替换成 `Pass`。

### 为什么这些项跑不了
1. **Content Patcher 未安装**（只在 Nexus 分发，本环境无法自动获取）→ 农场类型不会被注册 → 农场相关检查无法开始。
2. **没有现成存档，且无法非交互创建存档**：SMAPI 4.5.2 已移除 `debug quickstart`；新建存档需要点菜单选农场类型，
   属于交互操作，不能在无人值守环境里跑。伪造截图或测试结论是不允许的。

## 3. 性能

**未测**。没有进入存档，无法提供"同机同场景、效果开/关"的性能数据。
本版设计上的限制是可陈述的，不是实测结论：

- 任务推进：**唯一节流点**在 `ModEntry.OnUpdateTicked`，每 30 tick 才构造一次"完整快照"（含作物/动物统计），仅目标农场；
- 渲染回调（HUD / 雾效）使用 `ReadLightweight()`，**不统计作物与动物**，因此每帧不做全农场遍历；
- 渲染回调内无文件 IO、无任务判定；
- 雾效：最多 64 次 SpriteBatch 绘制（`MaxFogTiles`），随视口大小变化；
- 不注册 Harmony 补丁。

要填这一栏，需要先装好 Content Patcher 并进入一次主题农场，然后用 `SMAPI` 控制台与外部计时器对比开关前后。

## 4. 复现步骤

```powershell
# 1) 构建
dotnet build src/LeliaFarm.Core/LeliaFarm.Core.csproj

# 2) 单元测试
dotnet test tests/LeliaFarm.Tests/LeliaFarm.Tests.csproj

# 3) 部署到测试目录（默认 <游戏目录>\Mods）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy-test.ps1

# 4) 启动并读日志
& "<游戏目录>\StardewModdingAPI.exe"
Get-Content "$env:APPDATA\StardewValley\ErrorLogs\SMAPI-latest.txt" -Encoding UTF8 | Select-String "LeliaFarm|ERROR"
```

## 5. 后续实机清单（等 CP 就绪后按顺序做）

1. 安装 Content Patcher → 确认日志出现 `主题农场类型 LeliaFarm.Content_ManorFarm 已注册`。
2. 新建存档并选主题农场 → 确认地图不是黑图（即 §map-contract 未验证项 3 的答案）。
3. 确认农舍/出货箱/邮箱是否存在（未验证项 2）。
4. 挥锄翻地 → 确认 `Type=Dirt` 生效（未验证项 1 的延伸）。
5. 开 `EnableDevCommands` → 跑两次 `lelia_dev_setup` → 对比动物/建筑数量不变。
6. 交付：手持防风草到 (42,18) 交付 → 再点一次确认不再扣物。
7. 睡觉 → 退出 → 读档 → `lelia_dev_status` 确认状态一致。
8. 三档分辨率各截一张图，记录 UI 缩放，检查立绘与任务栏无重叠。
9. 关雾、删一张立绘、写坏一份清单，各验证一次降级与诊断。
10. 把结果补进上表，把 `Not Run` 改成真实结论。
