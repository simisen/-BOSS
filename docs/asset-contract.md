# 视觉资源契约（给 Codex）

目标：Codex 只需准备 **PNG / TMX / JSON**，按本文替换或重载即可生效，**不需要逐处改 C#**。

## 1. 资源 ID 与物理路径

稳定 ID 与文件路径分离。ID 发布后不改名；要换素材就换同名文件，或改清单里的 `source.path`。

| 资源 ID | 类型 | 当前文件 | 画布 | 采样 | 来源状态 |
|---|---|---|---|---|---|
| `portrait.lelia.neutral` | Portrait | `src/LeliaFarm.Core/assets/portraits/lelia_neutral.png` | 256×384 | LinearClamp | **placeholder** |
| `portrait.lelia.working` | Portrait | `.../lelia_working.png` | 256×384 | LinearClamp | **placeholder** |
| `portrait.lelia.tired` | Portrait | `.../lelia_tired.png` | 256×384 | LinearClamp | **placeholder** |
| `effects.fog.soft` | Fog | `.../assets/effects/fog_soft.png` | 256×256 | LinearClamp | **placeholder** |
| （HUD 底色） | — | `.../assets/ui/hud_panel.png` | 1×1 | PointClamp | placeholder（代码内固定引用，不在清单里） |
| 地图 | Map | `content/[CP] LeliaFarm/maps/manor_farm.tmx` | 64×40 格 | PointClamp | **placeholder（灰盒）** |
| 图块贴图 | TileSheet | `content/[CP] LeliaFarm/assets/tilesheets/lelia_farm_tiles.png` | 64×16（4 格） | PointClamp | **placeholder** |
| 农场选择图标 | — | `content/[CP] LeliaFarm/assets/ui/farm_icon.png` | 32×32 | — | **placeholder** |

清单文件：`src/LeliaFarm.Core/assets/visual-manifest.json`（Schema：`contracts/visual-manifest.schema.json`）。

## 2. 立绘规则

- **画布**：256×384（宽×高）。换尺寸必须同时改清单 `canvas`，否则帧校验会失败。
- **锚点**：`pivotNormalized = {x:0.5, y:1.0}`（脚底中央）。HUD 按"底边对齐"放置。
- **采样**：`LinearClamp`（高清立绘用平滑采样；像素素材才用 `PointClamp`）。
- **透明**：背景必须透明（`RenderedHud` 直接叠加）。
- **表情选择优先级**（实现在 `HudRenderer.SelectPortraitAsset`，只读游戏状态，不修改体力）：
  1. `tired`：体力 ≤ 20%
  2. `working`：体力 ≥ 90% 且当前有未完成任务
  3. `neutral`：其余情况
  - 未接入的 ID 会被降级为占位框，不报错、不中断 HUD。

## 3. 雾

- 256×256、四边自然淡出、可平铺观感；`provenance` 标 placeholder。
- 参数在清单 `sceneEffects.fog`：`opacity`（0–1，当前 0.10）、`speedWorldPixelsPerSecond`（当前 4）。
- **只在主题农场绘制**，离开地图立即停止；不改变全局天气。
- 当前实现只画在**世界上层**，**没有**地形遮挡。若需要遮挡效果，需要新的渲染方案（属 M4 范围）。

## 4. HUD 布局约定

| 项 | 默认 | 可配置项 | 约束（有单元测试） |
|---|---|---|---|
| 立绘锚点 | 左下 | `hud.portrait.anchor`（首版只接受 `BottomLeft`） | 不进入底部工具栏区域 |
| 立绘最大尺寸 | 宽 ≤18% 视口、高 ≤40% 视口 | `maxWidthFraction` / `maxHeightFraction` | 缩放后仍在视口内 |
| 安全边距 | 16 UI 像素 | `marginUiPixels`（0–512） | 只乘一次用户缩放 |
| 任务栏 | 右上，最多 3 项 | `hud.tasks.maxVisible`（0–5）、`maxWidthFraction` | 避开右上时钟/钱币保留区（72 UI 像素） |
| 互斥 | — | — | 空间不足先折叠立绘，再减少任务条数 |

**坐标注意**：不要照搬视频/截图里的右上角像素坐标。布局是从视口尺寸 + 工具栏位置算出来的，
分辨率或 UI 缩放变化会自动重算。UHD 与 1280×720 都有单元测试覆盖。

## 5. 加载与重载

| 操作 | 方法 |
|---|---|
| 换图（不改结构） | 直接覆盖同名 PNG → 返回标题再进游戏 |
| 改清单 | 编辑 `visual-manifest.json` → 游戏内执行 `lelia_dev_visuals`（先校验后整体切换，失败保留旧配置） |
| 换地图 | 替换 `maps/manor_farm.tmx` → **返回标题**重新加载（不承诺所有地图属性热重载） |
| 换图块贴图 | 替换 `assets/tilesheets/lelia_farm_tiles.png`，保持 64×16 与图块顺序 → 返回标题 |

校验规则（违反即整份清单不可用，旧配置保留）：
`schemaVersion=1`；`id` 唯一且非空；`kind` 必须属于枚举；`source.type` ∈ {ModFile, ContentAsset}；
路径不得含 `..`、不得是绝对路径、图片只接受 `.png`、Map 只接受 `.tmx/.tbin/.xnb`；
`canvas` 1–8192；`assetVersion ≥ 1`；`pivotNormalized` ∈ [0,1]；`provenance` 必填；
`frames` 名称唯一且矩形必须落在画布内；`hud.*` 与 `fog.*` 数值范围如上。

## 6. 原生角色图集

**本版未接入**（`IAppearanceAdapter` 未实现，停留在原版农夫渲染）。
因此这里**没有**记录图集实际像素尺寸、帧矩形、方向映射、手持工具关系和脚底锚点——
因为还没有实测结果。接入前不要用 `16×32` / `32×48` 之类的随手数字代替实测；
接入时必须先导出原版农夫图集并逐项测量，再写进本文件。

## 7. 命名建议（沿用任务书）

```text
portrait.lelia.neutral | portrait.lelia.working | portrait.lelia.tired | portrait.lelia.happy
ui.theme.frame | effects.fog.soft | map.manor.exterior | appearance.lelia.native
```

`portrait.lelia.happy` 与 `appearance.lelia.native` 目前**未在清单中登记**（没有资源、也没有代码路径使用）。
一旦提供素材，需要同时补清单条目和 `HudRenderer.SelectPortraitAsset` 的使用分支。

## 8. 来源与授权

`provenance` 字段只是**追踪元数据**，不是自动授权判定。placeholder 素材一律不得作为完成品发布。
不要使用官方商店图、视频截图或未授权素材充当已适配的游戏素材。
