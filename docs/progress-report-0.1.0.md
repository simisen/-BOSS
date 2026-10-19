# LeliaFarm 进度汇报（0.1.0）— 可直接转发

> 面向外部协作者（GPT）的自包含说明：不需要本次会话上下文即可接手。

## 1. 项目是什么

一个真实的 **Stardew Valley 1.6.15 Mod**：在旧修道院旁新增一座主题农场（Lelia / SiNiSistar 2 视觉参考），
支持原版耕作与养殖、三项引导任务、农产品交付、占位 HUD、农场雾效，可睡觉保存并继续读档。

- 作者：**AliceXitina**
- 版本：**0.1.0**
- 目标平台：Windows PC，单人，专用新农场存档
- 视觉：**全部为脚本生成的占位素材（灰盒）**，正式美术由 Codex 接手

## 2. 已完成里程碑

| 阶段 | 状态 | 说明 |
|---|---|---|
| M0 环境与契约 | ✅ 完成 | 版本冻结、API 反射核实、两份 JSON Schema、地图方案 |
| M1 可运行基础 | ✅ 完成 | 可构建 Core、日志、配置、存档隔离与迁移、清单校验、占位 HUD |
| M2 可玩灰盒 | ⚠️ 部分完成 | 代码/结构/单元测试齐备；**农场相关实机检查 Not Run**（缺 Content Patcher） |
| M3 接入准备 | ✅ 完成 | 资源契约、交接文档、占位素材与替换流程 |

## 3. 实测环境（已冻结）

| 组件 | 版本 | 备注 |
|---|---|---|
| Stardew Valley | **1.6.15**，FileVersion `1.6.15.24356`，Steam build `16826371` | 运行时 **net6.0**（决定了 TargetFramework） |
| SMAPI | **4.5.2**（`4.5.2+821167e5...`） | 要求游戏 ≥ 1.6.14；已安装并成功加载 |
| .NET SDK | **10.0.400** | 可构建 net6.0 目标 |
| Pathoschild.Stardew.ModBuildConfig | **4.4.0**（固定版本） | 构建不自动部署、不打包 zip |
| Content Patcher | **未安装** | 阻塞项，见 §6 |

### 已用反射核实的游戏 API（不是抄文档）

| 事实 | 入口 |
|---|---|
| 农场注册数据 | `Data/AdditionalFarms` → `Dictionary<string, ModFarmType>`；`ModFarmType` 字段：`Id / TooltipStringPath / MapName / IconTexture / WorldMapTexture / SpawnMonstersByDefault / ModData / CustomFields` |
| 农场类型判定 | `Game1.GetFarmTypeID()`（静态，返回 string） |
| 动物抚摸 | `FarmAnimal.wasPet`（NetBool）、`pet(Farmer, bool)` |
| 添加动物 | `Utility.addAnimalToFarm(FarmAnimal)` |
| 作物/耕地 | `Crop.dead`、`Crop.ctor(seedId, x, y, location)`、`HoeDirt.ctor(state, location)` |
| 建筑 | `Building.ctor(type, tile)`、`daysOfConstructionLeft` |
| 体力/工具栏 | `Farmer.Stamina`、`MaxStamina`；`Menus.Toolbar.yPositionOnScreen` |
| 地图格式支持 | SMAPI 模组内容管理器支持 `.fnt/.png/.tbin/.tmx` → 可直接加载 TMX 地图 |

## 4. 已实现的功能（代码 + 测试）

- **农场归属**：内容包（Content Patcher）通过 `Data/AdditionalFarms` 注册农场 `LeliaFarm.Content_ManorFarm`，地图 `Maps/LeliaFarm_Manor`。
  Core 启动时读一次 `Data/AdditionalFarms`：存在则启用，缺失则记录**一次**诊断并**安全闲置**（不生成农场、不改其他农场）。
  所有专属行为除判断"地点是 Farm"外，还会二次校验农场类型 ID。
- **三项引导任务**（配置驱动 `assets/tasks.json`，未知类型/重复 ID 一律拒绝整份配置）：
  - `intro.plant_5`：主题农场当前有效作物 ≥ 5 格即完成并锁定，不因收获回退，**不是**历史播种次数
  - `intro.pet_animal`：识别原版 `wasPet`，达成一次即完成
  - `intro.deliver_produce`：交付点提交 1 件配置内农产品，扣物后结算，只可成功一次
- **交付事务**：先全量校验再扣物；扣物后结算失败会**回退扣物**；`busy` + tick 间隔双保险防重复点击；奖励只在 `→ Claimed` 那一次转换发放。
- **存档**：`save-root` 键 + `schemaVersion` 显式迁移（0→1）；高版本 → `TooNew`（停写停奖励，原数据不动）；解析/迁移失败 → `Corrupt`（同上）；未知任务 ID 保留不执行；返回标题清空内存状态。
- **视觉资源**：稳定 ID 与路径分离；校验 `..` 穿越/绝对路径/枚型/版本/数值范围/重复 ID/帧越界；纹理缓存不 Dispose 共享资源；缺失则降级（立绘隐藏、雾关闭）。
- **HUD**：占位立绘 + 体力条 + ≤3 条任务；菜单/对话隐藏；F9 开关；布局由纯函数按视口与工具栏位置计算，三档分辨率有断言。
- **雾效**：仅主题农场；暂停时停止推进；离开地图/返回标题清理；绘制在世界上层（**未做地形遮挡，如实记录**）。
- **开发命令**（默认关闭，需 `EnableDevCommands: true`）：`lelia_dev_setup`（幂等生成样片，冲突即跳过不删既有对象）、`lelia_dev_status`、`lelia_dev_visuals`。
- **性能约束**：`Read()`（含作物/动物统计）只在 `UpdateTicked` 每 30 tick 调一次；HUD/雾效渲染走 `ReadLightweight()`，**不统计农场**，因此渲染回调不做全图遍历、无文件 IO。

## 5. 验证结果（真实）

**已通过**
- `dotnet build` 干净重建：0 错误 0 警告
- `dotnet test`：**49 项通过 / 0 失败**，覆盖：作物计数不重复累加与锁定、交付非法/重复/忙碌、奖励只发一次、存档迁移与高版本拒绝、坏 JSON 保留原文、未知任务保留、清单各类非法输入、HUD 三档分辨率布局、以及**"实物"校验**（直接解析随包的 tasks.json / visual-manifest.json / TMX 地图 / content.json）
- **实机启动**（SMAPI 4.5.2 + 游戏 1.6.15，2560×1440）：Core 正常加载（`Lelia Farm Core 0.1.0 by AliceXitina`），入口无异常，视觉清单与任务定义加载成功，约 10 秒到标题画面，无崩溃日志

**过程中被测试抓出并修复的真实缺陷**
1. 奖励规则错误：转入 `ReadyToClaim` 也发了钱（应仅 `→ Claimed`）
2. 地图 CSV 行分隔多一个逗号 → 每行 65 个值（应为 64）
3. HUD/雾效每帧调用完整快照 → 每帧遍历整个农场（违反"禁止每帧遍历世界"）
4. `Assets` 源码目录与 `assets` 内容目录在 Windows 大小写撞车，导致 `.cs` 被当素材打包

**Not Run（必须如实标注）**

| 检查 | 原因 |
|---|---|
| 新建主题农场、地图加载、耕地/播种/浇水 | 缺 Content Patcher |
| 猪鸡与畜舍、抚摸、回舍 | 缺 Content Patcher + 无存档 |
| 三项目标与交付实机行为 | 缺 Content Patcher + 无存档 |
| 睡觉/退出/读档/切换存档 | 无现成存档，且 SMAPI 4.5.2 已移除 `debug quickstart`，无法非交互建存档 |
| 四季加载、夜晚可读性 | 同上 |
| 1280×720 / 1920×1080 / 2560×1440 实际显示 | 只有布局纯函数断言，无截图 |
| 性能数据（效果开/关对比） | 无法进入存档同场景 |

## 6. 阻塞与未验证项（不掩饰）

1. **Content Patcher 未安装**：仅 Nexus 分发，GitHub 无发布资产，无人值守环境无法自动获取 → 农场相关实机检查全部 Not Run。这是当前唯一硬阻塞。
2. **地图没有 `Warp` 出口**：原版 `Farm.xnb` 是 LZX 压缩，离线取不到准确目标坐标；乱填会造成"看似能走、实际卡住"。补法已写入 `docs/map-contract.md` §6.1。
3. **自定义农场类型下农舍/出货箱/邮箱是否自动放置**：未验证。
4. **TMX 的 `<image source="Maps/lelia_farm_tiles">` 是否按内容资产名解析**：未验证；失败时改用 `.tbin`（`TileSheet.ImageSource` 可控）。
5. **池塘只是空草地**，无水面图块属性与碰撞。
6. **角色外观未接入**：`IAppearanceAdapter` 未实现，仍是原版农夫；原版图集的实际像素尺寸/帧矩形/方向映射/脚底锚点**尚未测量**，因此文档里没有这些数字。
7. 存档用强类型模型反序列化：写回时未知**顶层字段**会被丢弃（未知任务条目会保留）。
8. `lelia_dev_setup` 的建筑坐标（鸡舍 `(24,10)`、畜舍 `(34,10)`）由交付点偏移推出，未经实机确认；作物种子 ID 用旧式 `"472"`（防风草种子），限定 ID `(O)472` 等价性未验证。

## 7. 关键契约（外部接手需要）

| 项 | 值 |
|---|---|
| Core Mod ID | `LeliaFarm.Core` |
| 内容包 ID | `LeliaFarm.Content`（依赖 Content Patcher ≥ 2.0.0 + Core） |
| 农场类型 ID | `LeliaFarm.Content_ManorFarm` |
| 地图资产名 | `Maps/LeliaFarm_Manor` |
| 地图尺寸 | 64×40 格（16px），图层 `Back/Buildings/Paths/Front` |
| 交付入口 | **(42, 18)**，交互半径 1.6 格 |
| 作物区 | x 30–45, y 12–22（土壤图块带 `Type=Dirt`，可翻地） |
| 图块资产 | `Maps/lelia_farm_tiles`（64×16，4 格：0 透明 / 1 草地 / 2 土壤 / 3 石板路） |

## 8. 交付物清单

```
src/LeliaFarm.Core/          核心 Mod（SMAPI / net6.0）
  Domain/                    纯领域层（零游戏依赖，可脱机测试）
  Persistence/ Visuals/ Services/ Presentation/ GameAdapters/
  assets/ i18n/ manifest.json
content/[CP] LeliaFarm/      内容包：农场注册 + TMX 地图 + 贴图
contracts/                   visual-manifest.schema.json / tasks.schema.json + 示例
tests/LeliaFarm.Tests/       49 项纯逻辑测试
docs/                        environment / architecture / asset-contract
                             map-contract / testing / handoff-to-codex / CHANGELOG
scripts/                     install-smapi / deploy-test
                             generate-placeholder-art / generate-greybox-map
README.md
```

规模：源码 35 个 `.cs`、7 份文档、4 个契约 JSON、4 个脚本。

## 9. 建议下一步（按优先级）

1. **装 Content Patcher 2.x** → 新建主题农场 → 立刻能判定阻塞项 1/3/4 的真假。
2. 拿到 `Warp` 真值（Tiled + tbin→tmx 导出，或参考同版本自定义农场 mod）→ 补地图出口。
3. 实机跑完 `docs/testing.md` §5 的 10 步清单，把 `Not Run` 换成真实结论。
4. Codex 替换占位素材，逐项把 `provenance` 从 `placeholder` 改为 `final`。
5. 角色外观接入前，先实测导出原版农夫图集，再补 `docs/asset-contract.md` §6。

## 10. 需要外部协助的具体问题

1. Content Patcher 2.x 在 Stardew 1.6.15 下的**推荐版本号**，以及 TMX 地图里 `<image source>` 的**正确写法**（内容资产名 vs 相对路径）。
2. 1.6.15 自定义农场类型（`Data/AdditionalFarms`）下，**农舍/出货箱/邮箱**是游戏自动放置，还是必须在 `EditMap` 里手工放？
3. 原版 `Farm` 地图 `Warp` 属性的**标准取值**（目标地点名与坐标），用于补地图出口。
4. `new Crop(seedId, ...)` 在 1.6 中应传旧式 `"472"` 还是限定 ID `"(O)472"`。
