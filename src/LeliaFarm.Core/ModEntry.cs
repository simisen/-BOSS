namespace LeliaFarm.Core;

using LeliaFarm.Core.Visuals;
using LeliaFarm.Core.Configuration;
using LeliaFarm.Core.Diagnostics;
using LeliaFarm.Core.Domain.Tasks;
using LeliaFarm.Core.GameAdapters;
using LeliaFarm.Core.Persistence;
using LeliaFarm.Core.Presentation;
using LeliaFarm.Core.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData;

/// <summary>
/// Lelia 主题修道院农场 —— 核心 Mod 入口。
/// 职责：生命周期、存档隔离、任务与交付服务装配、占位 HUD 与场景特效、诊断。
/// 农场与地图的创建属于内容包 [CP] LeliaFarm；内容包缺失时本 Mod 安全闲置。
/// </summary>
public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private ModDiagnostics diagnostics = null!;
    private VisualAssetCatalog catalog = null!;
    private SaveStateStore saveStore = null!;
    private TaskService taskService = null!;
    private DeliveryService deliveryService = null!;
    private HudRenderer? hud;
    private SceneEffectsController? effects;
    private GameStateReader? stateReader;
    private TaskDefinitionSet taskDefinitions = new();
    private DevCommands? devCommands;

    private int scanTicks;
    private bool contentPackRegistered;
    private bool contentPackChecked;

    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        this.diagnostics = new ModDiagnostics(this.Monitor);

        try
        {
            this.config = helper.ReadConfig<ModConfig>();
            foreach (string note in this.config.Normalize())
                this.diagnostics.Warn("LeliaFarm 配置修正：" + note);
        }
        catch (Exception ex)
        {
            this.config = new ModConfig();
            this.diagnostics.Error($"LeliaFarm：config.json 读取失败，使用默认配置（{ex.Message}）。");
        }

        this.diagnostics.Info(
            $"LeliaFarm {this.ModManifest.Version} 启动：游戏 {Constants.GameVersionLabel}，"
            + $"内容包依赖 {ModConstants.ContentPackId}。作者：{this.ModManifest.Author}");

        // 视觉清单：先校验后使用；失败时降级（占位/HUD 隐藏/雾关闭）。
        this.catalog = new VisualAssetCatalog(helper, this.diagnostics);
        this.catalog.Initialize();

        this.taskDefinitions = this.LoadTaskDefinitions(helper);

        this.saveStore = new SaveStateStore(helper.Data, this.diagnostics, this.ModManifest.Version.ToString());
        this.taskService = new TaskService(this.taskDefinitions, this.saveStore, this.diagnostics, this.config, helper.Translation);

        this.stateReader = new GameStateReader(new ModConstantsProvider());
        this.deliveryService = new DeliveryService(
            this.taskService,
            this.config,
            this.diagnostics,
            helper.Translation,
            new DeliveryPoint { TileX = ModConstants.DeliveryTileX, TileY = ModConstants.DeliveryTileY });

        this.hud = new HudRenderer(helper, this.config, this.taskService, this.catalog, this.diagnostics, helper.Translation, this.stateReader);
        this.hud.Register();

        this.effects = new SceneEffectsController(helper, this.config, this.catalog, this.diagnostics, this.stateReader);
        this.effects.Register();

        this.devCommands = new DevCommands(helper, this.config, this.diagnostics, this.taskService, this.saveStore);
        this.devCommands.Register();

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }

    // ---- 事件 ----

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.diagnostics.OncePerSession("launched", LogLevel.Trace, "LeliaFarm：GameLaunched。");
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.diagnostics.ResetSaveScope();
        this.saveStore.Load();

        this.diagnostics.OncePerSave("save-loaded", LogLevel.Info,
            $"LeliaFarm：存档数据状态 {this.saveStore.Status}，可写入={this.saveStore.CanWrite}。");

        this.EnsureContentPackRegistered();

        if (!this.contentPackRegistered)
        {
            this.diagnostics.OncePerSave("farm-absent", LogLevel.Warn,
                $"LeliaFarm：未在 Data/AdditionalFarms 找到农场类型 {ModConstants.FarmTypeId}，"
                + "本 Mod 保持安全闲置（不生成农场、不修改其他农场）。请确认内容包 [CP] LeliaFarm 与 Content Patcher 已安装。");
        }
        else if (!GameStateReader.IsTargetFarmType())
        {
            this.diagnostics.OncePerSave("not-target-save", LogLevel.Info,
                $"LeliaFarm：当前存档农场类型为 '{Game1.GetFarmTypeID()}'，不是主题农场；本存档内不启用专属行为。");
        }
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.EnsureContentPackRegistered();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || this.saveStore.Root is null)
            return;

        if (!GameStateReader.IsTargetFarmType())
            return;

        // 唯一的限频点：完整快照会遍历农场作物与动物，绝不每帧做。
        if (++this.scanTicks < TaskService.ScanIntervalTicks)
            return;
        this.scanTicks = 0;

        var snapshot = this.stateReader?.Read();
        if (snapshot is null || !snapshot.OnTargetFarm)
            return;

        this.taskService.Refresh(snapshot);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.saveStore.Save();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        // 清理与存档关联的状态；事件本身在 Entry 里只注册一次，这里不做重复注册。
        this.saveStore.Unload();
        this.diagnostics.ResetSaveScope();
        this.effects?.Reset();
        this.contentPackChecked = false;
        this.contentPackRegistered = false;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || (e.Button != SButton.MouseLeft && e.Button != SButton.ControllerA))
            return;

        if (!this.config.EnableIntroTasks || Game1.activeClickableMenu is not null)
            return;

        if (!GameStateReader.IsTargetFarmType())
            return;

        this.deliveryService.TryDeliver(out _);
    }

    // ---- 装配辅助 ----

    /// <summary>确认内容包已把农场类型注册进 Data/AdditionalFarms。</summary>
    private void EnsureContentPackRegistered()
    {
        if (this.contentPackChecked)
            return;

        this.contentPackChecked = true;
        try
        {
            var farms = this.Helper.GameContent.Load<Dictionary<string, ModFarmType>>(ModConstants.AdditionalFarmsAsset);
            this.contentPackRegistered = farms.ContainsKey(ModConstants.FarmTypeId);

            if (this.contentPackRegistered)
            {
                this.diagnostics.OncePerSession("farm-registered", LogLevel.Info,
                    $"LeliaFarm：主题农场类型 {ModConstants.FarmTypeId} 已注册（地图 {ModConstants.FarmMapAssetName}）。");
            }
        }
        catch (Exception ex)
        {
            this.contentPackRegistered = false;
            this.diagnostics.OncePerSession("farm-load-failed", LogLevel.Error,
                $"LeliaFarm：读取 {ModConstants.AdditionalFarmsAsset} 失败：{ex.Message}");
        }
    }

    /// <summary>读取任务定义；文件缺失或非法时回退到内置默认三项，并给出一致诊断。</summary>
    private TaskDefinitionSet LoadTaskDefinitions(IModHelper helper)
    {
        string path = Path.Combine(helper.DirectoryPath, "assets", "tasks.json");
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            this.diagnostics.Error($"LeliaFarm：读取任务定义 {path} 失败（{ex.Message}），使用内置默认定义。");
            return BuiltInTaskDefinitions();
        }

        var result = TaskDefinitionSet.Parse(json);
        if (result.Ok && result.Set is not null)
        {
            this.diagnostics.OncePerSession("tasks-loaded", LogLevel.Info,
                $"LeliaFarm：任务定义已加载（schemaVersion={result.Set.SchemaVersion}，{result.Set.Tasks.Count} 条）。");
            return result.Set;
        }

        foreach (string error in result.Errors)
            this.diagnostics.Error("LeliaFarm 任务定义错误：" + error);

        this.diagnostics.Warn("LeliaFarm：任务定义未通过校验，使用内置默认定义（不执行非法配置）。");
        return BuiltInTaskDefinitions();
    }

    /// <summary>内置默认三项任务（与 contracts/tasks.example.json 保持一致）。</summary>
    private static TaskDefinitionSet BuiltInTaskDefinitions() => new()
    {
        SchemaVersion = TaskDefinitionSet.SupportedSchemaVersion,
        Tasks = new[]
        {
            new TaskDefinition
            {
                Id = "intro.plant_5",
                TitleKey = "task.plant5.title",
                ObjectiveType = ObjectiveType.CurrentCropCount,
                Target = 5,
                AutoClaim = true,
                Reward = new TaskReward { Gold = 0 }
            },
            new TaskDefinition
            {
                Id = "intro.pet_animal",
                TitleKey = "task.pet.title",
                ObjectiveType = ObjectiveType.PetAnimalToday,
                Target = 1,
                AutoClaim = true,
                Reward = new TaskReward { Gold = 0 }
            },
            new TaskDefinition
            {
                Id = "intro.deliver_produce",
                TitleKey = "task.deliver.title",
                ObjectiveType = ObjectiveType.DeliverProduce,
                Target = 1,
                AutoClaim = true,
                Reward = new TaskReward { Gold = 300 },
                AcceptedItemIds = new[] { "(O)24", "(O)188", "(O)186", "(O)184" }
            }
        }
    };
}

/// <summary>项目级稳定常量。写入存档前冻结，修改必须提供迁移方案。</summary>
public static class ModConstants
{
    /// <summary>本 Mod 的唯一 ID。</summary>
    public const string ModId = "LeliaFarm.Core";

    /// <summary>内容包（Content Patcher）的唯一 ID。</summary>
    public const string ContentPackId = "LeliaFarm.Content";

    /// <summary>主题农场的农场类型 ID（Data/AdditionalFarms 的键）。</summary>
    public const string FarmTypeId = "LeliaFarm.Content_ManorFarm";

    /// <summary>主题农场地图的资产名。</summary>
    public const string FarmMapAssetName = "Maps/LeliaFarm_Manor";

    /// <summary>农场注册数据资产名。</summary>
    public const string AdditionalFarmsAsset = "Data/AdditionalFarms";

    /// <summary>交付入口地块 X（与 docs/map-contract.md 一致）。</summary>
    public const int DeliveryTileX = 42;

    /// <summary>交付入口地块 Y（与 docs/map-contract.md 一致）。</summary>
    public const int DeliveryTileY = 18;
}

/// <summary>编译期环境标签，便于日志识别。</summary>
public static class Constants
{
    /// <summary>目标游戏版本标签（与 docs/environment.md 对应）。</summary>
    public const string GameVersionLabel = "1.6.15 (build 16826371, net6.0)";
}
