# LeliaFarm — 莉莉娅主题修道院农场（Stardew Valley Mod）

作者：**AliceXitina**

一个真实的星露谷 Mod：在旧修道院旁新增一座主题农场，支持原版耕作与养殖、三项引导任务、农交付、
占位 HUD 与环境雾效，可睡觉保存并继续读档。**当前版本是灰盒 + 占位素材**，视觉内容待 Codex 替换。

- 目标游戏：**Stardew Valley 1.6.15**（Steam build 16826371，net6.0）
- 依赖：**SMAPI 4.5.2**、**Content Patcher ≥ 2.0.0**
- 平台：Windows PC，单人，专用新农场存档
- 版本：`0.1.0`（M0/M1/M2 已交付，M3 接入准备已交付）

## 目前能做什么（已实现，有测试）

| 能力 | 状态 |
|---|---|
| Core Mod 在 SMAPI 下加载、无异常 | ✅ 实机验证 |
| 存档数据写入/读取/版本迁移/高版本只读保护 | ✅ 单元测试（非实机存档） |
| 三项引导任务的进度规则（锁定、不重复累加、奖励只发一次） | ✅ 单元测试 |
| 交付校验与防重复扣物/防重复奖励 | ✅ 单元测试 |
| 视觉清单校验（路径穿越、帧越界、重复 ID…） | ✅ 单元测试 |
| HUD 布局（三档分辨率、避让工具栏与右上保留区） | ✅ 单元测试（实际显示未验证） |
| 灰盒地图与内容包结构 | ✅ 结构校验（游戏内加载未验证） |
| 主题农场、耕地、养殖、任务、读档的**实机**检查 | ⛔ **Not Run**（本机缺 Content Patcher，见下） |

完整状态见 `docs/testing.md`，已知缺口见 `docs/CHANGELOG.md` 与 `docs/map-contract.md` §6。

## 安装（玩家）

1. 安装 **SMAPI 4.5.2**（或 ≥ 该版本的 4.x）。
2. 安装 **Content Patcher 2.x**（Nexus）。
3. 把 `LeliaFarm.Core` 与 `[CP] LeliaFarm` 两个文件夹一起放进 `Stardew Valley/Mods/`。
4. 用 SMAPI 启动游戏，新建存档时选择主题农场「Lelia 修道院农场」。

> ⚠️ 存档依赖内容包。**不要**直接删除内容包后继续该存档来"清理"——那会让地图资产缺失。
> 退出试用的正确做法是恢复测试前的存档备份，或继续使用原存档。

## 构建（开发者）

```powershell
# 1) 本机游戏路径：新建 local.props（不纳入发布）
#    <Project><PropertyGroup><GamePath>你的游戏目录</GamePath></PropertyGroup></Project>

# 2) 构建（不自动部署、不打包）
dotnet build src/LeliaFarm.Core/LeliaFarm.Core.csproj

# 3) 单元测试
dotnet test tests/LeliaFarm.Tests/LeliaFarm.Tests.csproj

# 4) 部署到测试目录
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy-test.ps1
```

环境变量 `LELIAFARM_GAMEPATH` 可覆盖 `local.props` 里的路径。

脚本一览与版本冻结详情：`docs/environment.md`。

## 目录

```text
src/LeliaFarm.Core/          核心 Mod（SMAPI / net6.0）
  Domain/                    纯领域层（零游戏依赖，可脱机测试）
  Persistence/               存档与版本迁移
  Visuals/                   视觉清单校验与资源目录
  Services/                  任务、交付、开发命令
  Presentation/              HUD 与雾效
  GameAdapters/              游戏状态 → 普通快照
  assets/ i18n/              随包资源与文案
content/[CP] LeliaFarm/      Content Patcher 内容包：农场注册 + TMX 地图 + 贴图
contracts/                   两份 JSON Schema + 示例
tests/LeliaFarm.Tests/       纯逻辑测试（49 项）
docs/                        环境、架构、资源契约、地图契约、测试、交接、变更
scripts/                     环境安装、部署、素材与地图生成
```

## 关键坐标（地图契约）

| 项 | 值 |
|---|---|
| 地图资产名 | `Maps/LeliaFarm_Manor` |
| 农场类型 ID | `LeliaFarm.Content_ManorFarm` |
| 尺寸 | 64×40 格（16 px 图块） |
| 交付入口 | **(42, 18)**，交互半径 1.6 格 |
| 作物区 | x 30–45, y 12–22 |

详见 `docs/map-contract.md`。

## 开发命令（需手动开启）

`config.json` 里设 `EnableDevCommands: true`（默认 `false`），然后：

| 命令 | 作用 |
|---|---|
| `lelia_dev_setup` | 幂等生成开发样片（鸡舍/畜舍、2 鸡 1 猪、5 格作物）；冲突时报告并跳过，不删既有对象 |
| `lelia_dev_status` | 打印任务与存档状态 |
| `lelia_dev_visuals` | 重新校验并重载视觉清单（失败保留旧配置） |

普通运行**不会**自动生成任何测试资源。

## 已知阻塞（诚实清单）

1. 本机未安装 **Content Patcher**（仅 Nexus 分发）→ 主题农场相关实机检查为 `Not Run`。
2. 地图**尚无 `Warp` 出口**：原版地图是 LZX 压缩 `xnb`，离线拿不到准确目标坐标。
3. 自定义农场类型下农舍/出货箱/邮箱是否自动放置：未验证。
4. TMX 的 tilesheet 引用是否按内容资产名解析：未验证（失败时改用 `.tbin`）。
5. 角色外观未接入，仍是原版农夫；`IAppearanceAdapter` 未实现，不算完成。
6. 性能数据未测。

## 协作边界

- 本仓库负责：基础架构、数据接口、原版玩法接入、存档、任务、构建打包、基础测试。
- Codex 负责：美术视觉方案、正式素材、视觉集成、后期检修（契约见 `docs/asset-contract.md`、`docs/handoff-to-codex.md`）。
- 两侧通过**仓库文件与文档**协作，不通过聊天上下文。

## 许可与素材

项目代码见上方作者与协作约定。**不要**把游戏原始 DLL、原版素材或 SiNiSistar 2 的任何资源放进发布包；
`assets/` 下现有图片都是脚本生成的占位图。
