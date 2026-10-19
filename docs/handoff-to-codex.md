# 交付给 Codex 的接入说明（M3 准备）

本文是交接清单：你现在要接的是**视觉**，不是业务逻辑。
业务代码不需要改一行——除非要新增资源 ID 或新的表情分支（见 §6）。

## 1. 实际资源 ID（当前清单里真实存在的）

| 资源 ID | kind | 文件（相对 Core Mod 根） | 画布 | 当前状态 |
|---|---|---|---|---|
| `portrait.lelia.neutral` | Portrait | `assets/portraits/lelia_neutral.png` | 256×384 | placeholder |
| `portrait.lelia.working` | Portrait | `assets/portraits/lelia_working.png` | 256×384 | placeholder |
| `portrait.lelia.tired` | Portrait | `assets/portraits/lelia_tired.png` | 256×384 | placeholder |
| `effects.fog.soft` | Fog | `assets/effects/fog_soft.png` | 256×256 | placeholder |

不在清单里、但代码直接引用的固定文件：

| 文件 | 尺寸 | 作用 |
|---|---|---|
| `assets/ui/hud_panel.png` | 1×1 纯白 | HUD 面板/体力条的着色底图（**不要改成非纯白**，颜色由代码 tint） |

内容包侧（`content/[CP] LeliaFarm`）：

| 文件 | 尺寸 | 作用 |
|---|---|---|
| `assets/tilesheets/lelia_farm_tiles.png` | 64×16（4 格 16×16） | 地图图块：0 透明 / 1 草地 / 2 土壤 / 3 石板路 |
| `assets/ui/farm_icon.png` | 32×32 | 农场选择界面图标 |
| `maps/manor_farm.tmx` | 64×40 格 | 灰盒地图 |

## 2. 清单文件

`src/LeliaFarm.Core/assets/visual-manifest.json`，Schema 在 `contracts/visual-manifest.schema.json`。
字段含义与取值范围见 `docs/asset-contract.md`。

**占位资源必须保留 `"provenance": "placeholder"`**，直到你确认素材可发布。
标成 `final` 之前请自查授权来源。

## 3. 替换流程

```powershell
# 1) 覆盖同名 PNG（保持画布尺寸不变）
# 2) 重新部署到测试目录
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy-test.ps1
# 3) 启动游戏；改清单的话在游戏内执行：
#    lelia_dev_visuals
#    改地图/图块的话：返回标题后重新进入
```

只换图不改结构时，也可以直接在 Mods 目录里覆盖文件后返回标题。

## 4. HUD 坐标约定

- 全部从**视口尺寸 + 工具栏位置**计算，不硬编码像素坐标。
- 立绘：左下，底边在工具栏上方，宽度 ≤ 视口 18%、高度 ≤ 视口 40%，边距 16 UI 像素。
- 任务栏：右上，避开顶部 72 UI 像素（时钟/钱币区），最多 3 条，宽度 ≤ 视口 25%。
- 只乘一次用户缩放（`config.HudScale`）；游戏 UI 缩放已体现在 `Game1.uiViewport` 里。
- 空间不足：先折叠立绘，再减少任务条数。

详细约束与三档分辨率断言见 `docs/asset-contract.md` §4 与 `tests/LeliaFarm.Tests/HudLayoutTests.cs`。

## 5. 地图范围

- 尺寸 64×40 格（1024×640 px）。
- 作物区 `x 30–45, y 12–22`；主路 `y=18, x 8–56`；竖路 `x=22, y 6–18`。
- 交付点 **(42,18)**，交互半径 1.6 格。
- 林地预留 `x 4–26, y 26–36`；池塘预留 `x 50–58, y 26–34`（当前只是空草地，没有水面属性）。
- 保护区域与"未验证项"见 `docs/map-contract.md` §3 与 §6。

## 6. 需要改 C# 的情况（少数）

| 需求 | 要改的地方 |
|---|---|
| 新增立绘表情并让 HUD 使用 | 清单加条目 + `HudRenderer.SelectPortraitAsset` 加分支 |
| 任务栏显示图标而不是文字 | `HudRenderer.DrawTasks` |
| 雾的绘制方式（改为地形遮挡、多层） | `SceneEffectsController.DrawFog` |
| 换 HUD 底色文件路径 | `HudRenderer.TryLoadPixel` |

其余（换图、改尺寸参数、改雾透明度/速度、改任务文案、改任务数值）都只是改 JSON/PNG。

## 7. 截图复现条件

要让截图可复现，需要同时记录：

1. 游戏版本 1.6.15、SMAPI 4.5.2、`Pathoschild.Stardew.ModBuildConfig` 4.4.0；
2. 分辨率与 UI 缩放（`Game1.options.uiScale`）；
3. 存档是**新建主题农场**，且是专用测试存档；
4. 是否开了 `EnableDevCommands`、是否跑过 `lelia_dev_setup`；
5. 雾效开关（`config.json` 的 `EnableFog`）。

## 8. 目前仍是占位的东西（别当成完成品）

- 三张立绘、雾贴图、HUD 底色、图块贴图、农场图标：全部是脚本生成的纯色/剪影占位。
- 地图：灰盒，只有草地/土壤/石板路三种植被，没有任何装饰、建筑美术、水体。
- `portrait.lelia.happy`、`appearance.lelia.native`：**只存在于命名建议里**，清单和代码都没有。
- 角色外观（农夫图集）：**完全没有接入**，当前显示原版农夫。`IAppearanceAdapter` 未实现，不算视觉功能完成。
- 原版农夫图集的实际像素尺寸/帧矩形/方向映射/脚底锚点：**尚未测量**，所以本文没有写这些数字。
  接入前必须先实测导出，再把结果写进 `docs/asset-contract.md` §6。

## 9. 交接状态一句话

Core 与内容包的结构、契约、降级路径都已就位并通过单元测试与一次实机加载；
**视觉层全部是占位**，你要做的是把 §8 的清单逐项换成真素材，并推动 §map-contract 里的四个未验证项拿到实测结论。
