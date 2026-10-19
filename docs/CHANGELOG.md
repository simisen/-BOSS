# CHANGELOG

## 0.1.0 — 2026-10-19 — M0 / M1 / M2 基础架构 + M3 接入准备

### 项目骨架
- 新增 `src/LeliaFarm.Core`（net6.0，SMAPI Mod），固定依赖 `Pathoschild.Stardew.ModBuildConfig 4.4.0`。
- 新增 `content/[CP] LeliaFarm`（Content Patcher 内容包，`Format: 2.0.0`）。
- 新增 `contracts/`：`visual-manifest.schema.json`、`tasks.schema.json` 及两份示例。
- 新增 `tests/LeliaFarm.Tests`：49 项纯逻辑测试，全部通过。
- 新增 `scripts/`：`install-smapi.ps1`、`deploy-test.ps1`、`generate-placeholder-art.ps1`、`generate-greybox-map.ps1`。

### 已实现
- **环境与契约**：`docs/environment.md` 记录实测版本（游戏 1.6.15 / SMAPI 4.5.2 / .NET SDK 10.0.400 / net6.0）。
- **游戏适配**：`IGameStateReader` + `GameStateReader`，把游戏状态翻成 `GameSnapshot`；纯领域层不接触 `Game1`。
- **存档**：`SaveStateStore` + `SaveRoot` + `SaveMigration`
  - 键 `save-root`；`schemaVersion` 显式迁移（0 → 1）；
  - 高版本 → `TooNew`：停止写入与奖励，原始数据不动；
  - 解析/迁移失败 → `Corrupt`：同样停止写入；
  - 未知任务 ID 保留、不执行；返回标题清空内存状态。
- **三项引导任务**：可配置 `assets/tasks.json`（未知 objectiveType / scope / 重复 ID 一律拒绝整份配置）。
  - `intro.plant_5`：主题农场当前有效作物 ≥ 5 → 达成即 Claimed；锁定后不因收获回退；
  - `intro.pet_animal`：识别原版 `FarmAnimal.wasPet`，达成一次即 Claimed；
  - `intro.deliver_produce`：交付入口提交 1 件配置内农产品，扣物后结算，只可成功一次。
- **交付**：`DeliveryService` 先全量校验再扣物；扣物后结算失败会回退扣物；`busy` + tick 间隔双保险防重复触发。
- **视觉**：`VisualManifestValidator`（路径穿越/枚举/版本/范围/重复 ID/帧越界）+ `VisualAssetCatalog`（缓存、按资源去重诊断、受控重载）。
- **HUD**：占位立绘 + 体力条 + 最多 3 条任务；菜单/对话时隐藏；F9 可隐藏；布局由纯函数按视口与工具栏位置计算。
- **雾效**：仅主题农场；暂停时停止推进；离开地图与返回标题清理状态；绘制在世界上层（未做地形遮挡）。
- **开发命令**（默认关闭，需 `EnableDevCommands=true`）：
  - `lelia_dev_setup`：幂等生成鸡舍/畜舍、2 鸡 1 猪、5 格作物；冲突时报告并跳过，不删既有对象；
  - `lelia_dev_status`：打印任务与存档状态；
  - `lelia_dev_visuals`：先校验后整体切换视觉清单。
- **内容包**：`Data/AdditionalFarms` 注册 `LeliaFarm.Content_ManorFarm`，`Load` 地图 `Maps/LeliaFarm_Manor` 与图块 `Maps/lelia_farm_tiles`。
- **地图**：64×40 灰盒 TMX，四图层，土壤图块带 `Type=Dirt`，交付点 (42,18)。

### 性能相关实现约束
- `IGameStateReader` 分两个入口：`Read()`（完整快照，含农场作物/动物统计）与 `ReadLightweight()`（渲染用，无统计）。
- 任务推进只有**一个**节流点：`ModEntry.OnUpdateTicked` 每 `TaskService.ScanIntervalTicks`（30 tick ≈ 0.5 s）。
- HUD 与雾效的渲染回调走 `ReadLightweight()`，不做全农场遍历、不做文件 IO。

### 验证结果（真实）
- `dotnet test`：**49 通过 / 0 失败**。
- 实机（SMAPI 4.5.2 + 游戏 1.6.15，2560×1440）：Core 正常加载、入口无异常、视觉清单与任务定义加载成功、到达标题画面。
- 内容包在本机被 SMAPI 跳过（缺 Content Patcher）——符合预期，且 Core 未因此报错。

### 已知问题 / 未验证（不算完成）
1. **Content Patcher 未安装** → 主题农场、地图、耕地、养殖、任务、读档等实机检查全部 `Not Run`。
2. **地图没有 `Warp` 出口**：原版 `Farm.xnb` 为 LZX 压缩，无法离线取准确目标坐标，故未编写；补法见 `docs/map-contract.md` §6.1。
3. **自定义农场类型下农舍/出货箱/邮箱的自动放置未验证**（§6.2）。
4. **TMX `<image source="Maps/...">` 是否按内容资产名解析未验证**（§6.3）；失败时的替代方案是改用 `.tbin`。
5. **池塘只是空草地**，没有水面图块属性与碰撞。
6. **HUD 实际显示效果未验证**（无存档、无截图）；只有布局纯函数的三档分辨率断言。
7. **性能数据未测**（无法进入存档同场景对比）。
8. **角色外观未接入**：`IAppearanceAdapter` 未实现，停留在原版农夫渲染。
9. 存档为强类型模型反序列化：写回时**未知顶层字段**会被丢弃（未知任务条目会保留）。若要严格保留未知字段，需要改为原始 JSON 保真写入。
10. `lelia_dev_setup` 的建筑坐标（鸡舍 `(24,10)`、畜舍 `(34,10)`）是按交付点偏移推出的，**未经实机确认**是否落在可用空地。
11. 作物样片使用种子 ID `"472"`（防风草种子，旧式数字 ID）；1.6 的限定 ID（`(O)472`）是否等价**未在游戏内验证**。

### 下一步（M3 之后）
- 安装 Content Patcher → 完成 `docs/testing.md` §5 的 10 步实机清单。
- 拿到 `Warp` 真值 → 补地图出口。
- Codex 替换占位素材 → 逐项把 `provenance` 改为 `final`。
- 角色外观接入前，先实测导出原版农夫图集并补 `docs/asset-contract.md` §6。
