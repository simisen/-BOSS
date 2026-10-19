# 架构（M0/M1）

## 1. 总体结构与依赖方向

```text
LeliaFarm（工作目录 = 仓库根）
  src/LeliaFarm.Core/          ← SMAPI Mod（net6.0），业务逻辑
  content/[CP] LeliaFarm/      ← Content Patcher 内容包：农场注册 + 地图 + 贴图
  contracts/                   ← 两份 JSON Schema + 示例（跨人协作契约）
  tests/LeliaFarm.Tests/       ← 纯逻辑测试（不引用游戏程序集）
  docs/  scripts/  README.md
```

依赖方向（单向，无循环）：

```text
Data/AdditionalFarms + Maps/LeliaFarm_Manor
        ↑ 由内容包（CP）注册
[CP] LeliaFarm ──依赖──> Content Patcher
        │
        └──依赖──> LeliaFarm.Core（仅通过"农场类型 ID 存在"这一数据契约耦合；Core 不引用内容包代码）

LeliaFarm.Core ──依赖──> SMAPI 4.5.2 ──> Stardew Valley 1.6.15
```

- **Core 不强制依赖内容包**：内容包缺失时 Core 记录一次诊断并安全闲置，不生成农场、不修改其他农场。
- 内容包**依赖** CP 与 Core（`manifest.json` 的 `Dependencies` 为 `IsRequired: true`）。
- 两者之间没有任何 C# 类型引用，唯一耦合点是常量 `LeliaFarm.Content_ManorFarm`（农场类型 ID）。

## 2. 目录与职责

```text
src/LeliaFarm.Core/
  ModEntry.cs                       入口：生命周期、装配、事件绑定（全局只绑定一次）
  ModConstants（同文件）            项目 ID / 农场类型 ID / 地图资产名 / 交付点坐标
  Configuration/ModConfig.cs        用户配置 + 范围修正
  Diagnostics/ModDiagnostics.cs     日志去重（进程级 / 存档级两档）
  GameAdapters/                     读游戏状态 → 普通快照；纯领域层不接触 Game1
    IGameStateReader.cs
    GameStateReader.cs
  Domain/                           纯领域层（零游戏依赖，可脱机测试）
    Tasks/ObjectiveType.cs          任务状态机与目标类型
    Tasks/TaskDefinition.cs
    Tasks/TaskDefinitionSet.cs      配置解析 + 校验（拒绝未知类型/重复 ID）
    Tasks/TaskRuntimeState.cs
    Tasks/TaskProgressEngine.cs     进度规则（latch、自动结算、奖励只发一次）
    Delivery/DeliveryRules.cs       交付校验（失败原因枚举化）
    Hud/HudLayoutCalculator.cs      HUD 矩形计算（纯函数）
  Delivery/DeliveryItemSnapshot.cs  手持物品快照（普通数据）
  Persistence/
    SaveRoot.cs                     存档根对象 + 版本迁移（纯逻辑）
    SaveStateStore.cs               SMAPI 存档数据读写 + 只读保护
  Visuals/                          视觉资源清单、校验、缓存
    VisualManifest.cs
    VisualManifestValidator.cs      纯校验（枚举/版本/范围/重复 ID/帧越界/路径穿越）
    VisualAssetCatalog.cs           SMAPI 侧加载与降级
  Services/
    TaskService.cs                  三项任务推进 + 奖励发放
    DeliveryService.cs              交付事务（扣物+结算+回滚）
    DevCommands.cs                  lelia_dev_* 开发命令（默认关闭、幂等）
  Presentation/
    HudRenderer.cs                  占位 HUD（体力条 + ≤3 任务 + 立绘）
    SceneEffectsController.cs       目标农场雾效与生命周期清理
  assets/                           随包资源（tasks.json、visual-manifest.json、占位图）
  i18n/default.json                 默认文案（当前为中文）
```

## 3. 关键设计决定

### 3.1 领域层与游戏层分离
`Domain/` 与 `Persistence/SaveRoot.cs`、`Visuals/VisualManifest*.cs` **不引用 SMAPI 或 xTile**。
测试工程用 `<Compile Include="...">` 直接链接这些源码，因此领域规则可以在没有任何游戏进程的情况下测试。
这是"领域层应能脱离游戏进程测试"的实现方式，也避免了额外拆分 DLL。

### 3.2 农场归属：内容包建农场，Core 管行为
按任务书要求，农场与地图由内容包通过 `Data/AdditionalFarms` + `Maps/*` 注册。
Core 启动后在存档载入时读一次 `Data/AdditionalFarms`：
- 存在目标农场类型 → 正常启用；
- 不存在 → 记录**一次**诊断并进入闲置（不创建农场、不改其他农场）。

所有专属行为除了看"当前地点是 Farm"之外，还**必须**再校验 `Game1.GetFarmTypeID() == LeliaFarm.Content_ManorFarm`，
因为运行时地点名 `Farm` 本身不代表主题农场。

### 3.3 状态更新来源（SMAPI 事件，非每帧全图扫描）
| 事件 | 用途 |
|---|---|
| `GameLoop.SaveLoaded` | 读存档数据、确认内容包注册、进入/退出闲置 |
| `GameLoop.DayStarted` | 复核内容包注册（新一天） |
| `GameLoop.UpdateTicked` | 限频推进任务（见下） |
| `GameLoop.Saving` | **只有真实保存流程**写持久数据 |
| `GameLoop.ReturnedToTitle` | 清空存档相关状态、雾效状态、去重键、注册缓存 |
| `Input.ButtonPressed` | HUD 隐藏键、交付交互 |
| `Display.RenderedHud` | 只绘制已有状态（无 IO、无判定） |
| `Display.Rendered` | 雾效（世界空间），仅目标农场 |

任务推进的限频策略：`UpdateTicked` 里计数，**每 30 tick（约 0.5 s）** 才扫描一次，
且只在"玩家站在主题农场 + 存在未终结任务"时扫描；扫描范围是农场自身 `terrainFeatures`
与 `getAllFarmAnimals()`，不是整个世界。原因：没有可用的"种植/抚摸"细粒度事件。

### 3.4 交付事务（防重复、防半完成）
1. `DeliveryRules.Check(...)` 先把所有条件判完（含 `busy` 与 `Claimed`）；
2. 同一主线程内：扣 1 件 → 结算任务 → 发 HUD 提示；
3. 任一步抛异常 → **回退扣物**（`Stack` 复原 / 放回背包），返回 `RolledBack`；
4. 同一次按键再触发时，`lastAttemptTick` 与 `busy` 双保险拦截。
奖励只在 `→ Claimed` 那一次转换发放（`TaskTransition.GoldGranted`），因此重载、重复点击都不会重复给。

### 3.5 存档与版本
- 只用 SMAPI `IDataHelper.ReadSaveData/WriteSaveData`，键 `save-root`；
- 根对象 `SchemaVersion`；任务按稳定 ID 存 `Dictionary<string, TaskRuntimeState>`；
- `schemaVersion > 当前` → `TooNew`：**停止写入与奖励**，原始数据保持不动；
- 解析失败/迁移失败 → `Corrupt`：同样停止写入，并在日志里保留原因；
- 未知任务 ID 原样保留、不执行；
- `ReturnedToTitle` 清空内存状态，防止跨存档串数据。
- 已知限制：使用强类型模型反序列化，写回时**未知顶层字段会被丢弃**（未知任务条目会保留）。见 `docs/CHANGELOG.md` 的已知问题。

### 3.6 视觉资源
- 稳定 ID 与物理路径分离（`portrait.lelia.neutral` → `assets/portraits/lelia_neutral.png`）；
- 路径限制在 Mod 内容根目录内，拒绝 `..`、绝对路径（`VisualManifestValidator`）；
- 纹理只在加载/受控重载时读一次并缓存；**不 Dispose** 游戏内容管理器共享纹理；
- 可选资源缺失 → 降级（立绘隐藏/占位框、雾关闭），错误按资源 ID 去重记录；
- 开发命令重载：先校验候选清单，成功后整体切换，失败保留旧配置。

### 3.7 HUD 布局
`HudLayoutCalculator` 是纯函数，输入全部是**UI 像素**（已由游戏 UI 缩放换算过），
只乘一次用户缩放。约束（有单元测试断言）：
- 立绘与任务栏都在视口内；
- 立绘不进入底部工具栏区域；
- 任务栏不在右上角时钟/钱币保留区内；
- 立绘与任务栏互不重叠；空间不足时优先折叠立绘/减少任务条数。

### 3.8 渲染约束
- 不在渲染回调里做文件 IO、任务判定或大量临时分配；
- 不改动游戏的 `SpriteBatch` 状态（不自行 `End/Begin`）；
- 雾效绘制在**世界上层**，不做地形遮挡——如实记录，不声称已实现遮挡。

### 3.9 未使用 Harmony
首版没有需要补丁的入口：农场注册走数据资产，任务/交付走公开事件与公开 API。
因此无 Harmony 补丁、无目标方法签名风险、无卸载回退问题。

## 4. 与任务书接口表的对应

| 任务书建议接口 | 本实现 | 说明 |
|---|---|---|
| `IGameStateReader` | `GameAdapters/IGameStateReader.cs` + `GameStateReader.cs` | 输出 `GameSnapshot` 普通 DTO |
| `ISaveStateStore` | `Persistence/SaveStateStore.cs` | 含 `TooNew`/`Corrupt` 只读保护 |
| `ITaskService` | `Services/TaskService.cs`（实现 `ITaskService`） | 不绘制，只维护状态 |
| `IDeliveryService` | `Services/DeliveryService.cs` | 校验→扣物→结算→回滚 |
| `IVisualAssetCatalog` | `Visuals/VisualAssetCatalog.cs` | 解析/校验/缓存/失效 |
| `IAppearanceAdapter` | **未实现** | 首版停留在原版回退（任务书允许）；不挂名空实现 |
| `IHudLayoutService` | `Domain/Hud/HudLayoutCalculator.cs`（静态纯函数） | 没有独立服务类，避免过度分层 |
| `ISceneEffectsController` | `Presentation/SceneEffectsController.cs` | 仅目标农场、可清理 |

`IAppearanceAdapter` 明确**未实现**：角色图集尚未接入，本版停留在原版农夫渲染，不把空接口算作视觉功能完成。
