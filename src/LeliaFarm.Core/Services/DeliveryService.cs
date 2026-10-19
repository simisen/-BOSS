namespace LeliaFarm.Core.Services;

using LeliaFarm.Core.Configuration;
using LeliaFarm.Core.Delivery;
using LeliaFarm.Core.Diagnostics;
using LeliaFarm.Core.Domain.Delivery;
using LeliaFarm.Core.Domain.Tasks;
using LeliaFarm.Core.GameAdapters;
using StardewModdingAPI;
using StardewValley;

/// <summary>交付结果（用于日志与测试断言）。</summary>
public enum DeliveryOutcome
{
    /// <summary>成功：已扣 1 件并结算任务。</summary>
    Delivered = 0,

    /// <summary>不在交付入口附近。</summary>
    NotAtDeliveryPoint = 1,

    /// <summary>不在主题农场。</summary>
    NotOnTargetFarm = 2,

    /// <summary>没有可交付的任务。</summary>
    NoDeliverTask = 3,

    /// <summary>校验未通过（见 <see cref="DeliveryCheck"/>）。</summary>
    Rejected = 4,

    /// <summary>扣物后结算失败，已回退扣物。</summary>
    RolledBack = 5
}

/// <summary>交付入口定义（地块坐标 + 交互半径，单位：格）。</summary>
public sealed class DeliveryPoint
{
    /// <summary>地块 X。</summary>
    public int TileX { get; init; }

    /// <summary>地块 Y。</summary>
    public int TileY { get; init; }

    /// <summary>交互半径（格）。</summary>
    public float RadiusTiles { get; init; } = 1.6f;

    /// <summary>玩家所在地块是否在交付入口范围内。</summary>
    public bool IsPlayerWithin(StardewValley.Farmer player)
    {
        if (player is null)
            return false;

        float dx = player.Tile.X - this.TileX;
        float dy = player.Tile.Y - this.TileY;
        return (dx * dx) + (dy * dy) <= this.RadiusTiles * this.RadiusTiles;
    }
}

/// <summary>交付服务接口。校验、扣物、奖励在同一主线程操作内完成。</summary>
public interface IDeliveryService
{
    /// <summary>尝试把当前手持物品交付给引导任务。</summary>
    DeliveryOutcome TryDeliver(out string message);
}

/// <summary>
/// 交付实现。防重复：同一 tick 内的重复事件、以及"已完成"状态都不会二次扣物或二次给奖励；
/// 扣物与结算之间失败会回退扣物，避免半完成交易。
/// </summary>
public sealed class DeliveryService : IDeliveryService
{
    private readonly TaskService tasks;
    private readonly ModConfig config;
    private readonly ModDiagnostics diagnostics;
    private readonly ITranslationHelper translation;
    private readonly DeliveryPoint point;

    private bool busy;
    private int lastAttemptTick = -1000;

    /// <summary>构造。</summary>
    public DeliveryService(
        TaskService tasks,
        ModConfig config,
        ModDiagnostics diagnostics,
        ITranslationHelper translation,
        DeliveryPoint point)
    {
        this.tasks = tasks;
        this.config = config;
        this.diagnostics = diagnostics;
        this.translation = translation;
        this.point = point;
    }

    /// <summary>交付入口。</summary>
    public DeliveryPoint Point => this.point;

    /// <inheritdoc />
    public DeliveryOutcome TryDeliver(out string message)
    {
        message = string.Empty;

        if (!Context.IsWorldReady || Game1.player is null)
            return DeliveryOutcome.NotOnTargetFarm;

        if (Game1.currentLocation is not Farm || !GameStateReader.IsTargetFarmType())
            return DeliveryOutcome.NotOnTargetFarm;

        if (!this.point.IsPlayerWithin(Game1.player))
            return DeliveryOutcome.NotAtDeliveryPoint;

        var definition = this.tasks.DeliverTask;
        if (definition is null)
        {
            this.diagnostics.OncePerSave("deliver-no-task", LogLevel.Warn,
                "LeliaFarm：没有配置 DeliverProduce 类型的任务，交付入口保持静默。");
            return DeliveryOutcome.NoDeliverTask;
        }

        var state = this.tasks.GetState(definition.Id);
        if (state is null)
            return DeliveryOutcome.NoDeliverTask;

        // 防重复：同一次按下可能触发多次事件。
        int tick = Game1.ticks;
        if (this.busy || tick - this.lastAttemptTick < 10)
            return DeliveryOutcome.Rejected;
        this.lastAttemptTick = tick;

        var held = GameStateReader.ReadHeldItem();
        var slot = held is null
            ? (InventorySlotSnapshot?)null
            : new InventorySlotSnapshot(held.QualifiedItemId, held.Stack, held.Quality);

        DeliveryCheck check = DeliveryRules.Check(definition, state, slot, this.busy);
        if (check != DeliveryCheck.Ok)
        {
            message = this.DescribeRejection(check, definition, held);
            Game1.addHUDMessage(new HUDMessage(message));
            return DeliveryOutcome.Rejected;
        }

        this.busy = true;
        try
        {
            var item = Game1.player.CurrentItem;
            if (item is null || item.Stack < 1)
            {
                message = this.DescribeRejection(DeliveryCheck.NoItemHeld, definition, held);
                return DeliveryOutcome.Rejected;
            }

            string itemName = item.DisplayName;
            int stackBefore = item.Stack;

            // 1) 扣物
            item.Stack -= 1;
            if (item.Stack <= 0)
                Game1.player.removeItemFromInventory(item);

            try
            {
                // 2) 结算（同一主线程操作内完成）
                var transition = this.tasks.CompleteDeliverTask(Game1.Date.TotalDays);
                if (transition is null)
                    throw new InvalidOperationException("交付任务状态结算返回 null。");

                message = this.translation.Get("delivery.success", new { item = itemName }).ToString();
                Game1.addHUDMessage(new HUDMessage(message));
                return DeliveryOutcome.Delivered;
            }
            catch (Exception ex)
            {
                // 3) 回退扣物：宁可退回，也不留半完成交易。
                item.Stack = stackBefore;
                if (!Game1.player.Items.Contains(item))
                    Game1.player.addItemToInventory(item);

                this.diagnostics.OncePerSave("deliver-rollback", LogLevel.Error,
                    $"LeliaFarm：交付结算失败并已回退扣物：{ex.Message}");
                message = this.translation.Get("delivery.rollback").ToString();
                Game1.addHUDMessage(new HUDMessage(message));
                return DeliveryOutcome.RolledBack;
            }
        }
        finally
        {
            this.busy = false;
        }
    }

    private string DescribeRejection(DeliveryCheck check, TaskDefinition definition, DeliveryItemSnapshot? held) => check switch
    {
        DeliveryCheck.AlreadyClaimed => this.translation.Get("delivery.alreadyDone").ToString(),
        DeliveryCheck.TaskNotActive => this.translation.Get("delivery.notActive").ToString(),
        DeliveryCheck.NoItemHeld => this.translation.Get("delivery.noItem").ToString(),
        DeliveryCheck.ItemNotAccepted => this.translation
            .Get("delivery.notAccepted", new { item = held?.DisplayName ?? "?" }).ToString(),
        DeliveryCheck.InsufficientQuantity => this.translation.Get("delivery.noItem").ToString(),
        DeliveryCheck.Busy => this.translation.Get("delivery.busy").ToString(),
        _ => this.translation.Get("delivery.notAccepted", new { item = definition.TitleKey }).ToString()
    };
}
