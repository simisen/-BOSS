namespace LeliaFarm.Core.Domain.Delivery;

using LeliaFarm.Core.Domain.Tasks;

/// <summary>背包槽位快照（纯数据，不含游戏对象）。</summary>
public readonly struct InventorySlotSnapshot
{
    /// <summary>构造一个槽位快照。</summary>
    /// <param name="itemId">物品的限定 ID。</param>
    /// <param name="stack">数量。</param>
    /// <param name="quality">品质（0 普通 … 4 铱星）。</param>
    public InventorySlotSnapshot(string itemId, int stack, int quality)
    {
        this.ItemId = itemId;
        this.Stack = stack;
        this.Quality = quality;
    }

    /// <summary>物品限定 ID。</summary>
    public string ItemId { get; }

    /// <summary>数量。</summary>
    public int Stack { get; }

    /// <summary>品质。</summary>
    public int Quality { get; }
}

/// <summary>交付校验结果。</summary>
public enum DeliveryCheck
{
    /// <summary>可以提交。</summary>
    Ok = 0,

    /// <summary>任务尚未启用（Locked）。</summary>
    TaskNotActive = 1,

    /// <summary>任务已经完成/领取过。</summary>
    AlreadyClaimed = 2,

    /// <summary>当前没有手持物品。</summary>
    NoItemHeld = 3,

    /// <summary>手持物品不在允许列表内。</summary>
    ItemNotAccepted = 4,

    /// <summary>数量不足 1。</summary>
    InsufficientQuantity = 5,

    /// <summary>存在同一时刻未结束的交付操作（防重复点击）。</summary>
    Busy = 6
}

/// <summary>
/// 交付规则（纯逻辑）。所有判定先于任何扣除执行；扣除与奖励在服务层同一主线程操作中完成，
/// 因此不会出现"扣一半"的半完成交易。
/// </summary>
public static class DeliveryRules
{
    /// <summary>
    /// 判断当前手持物品能否用于交付。
    /// </summary>
    /// <param name="definition">交付型任务定义。</param>
    /// <param name="state">任务运行时状态。</param>
    /// <param name="held">手持物品快照；null 表示空手。</param>
    /// <param name="isBusy">是否已有交付事务在进行。</param>
    public static DeliveryCheck Check(
        TaskDefinition definition,
        TaskRuntimeState state,
        InventorySlotSnapshot? held,
        bool isBusy = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        if (definition.ObjectiveType != ObjectiveType.DeliverProduce)
            throw new InvalidOperationException($"任务 {definition.Id} 不是交付型任务。");

        if (isBusy)
            return DeliveryCheck.Busy;

        if (state.State == TaskState.Claimed)
            return DeliveryCheck.AlreadyClaimed;

        if (state.State == TaskState.Locked)
            return DeliveryCheck.TaskNotActive;

        if (held is null)
            return DeliveryCheck.NoItemHeld;

        var slot = held.Value;
        if (slot.Stack < 1)
            return DeliveryCheck.InsufficientQuantity;

        if (!definition.AcceptedItemIds.Contains(slot.ItemId, StringComparer.Ordinal))
            return DeliveryCheck.ItemNotAccepted;

        return DeliveryCheck.Ok;
    }

    /// <summary>是否需要玩家显式领取（自动结算关闭且已达成）。</summary>
    public static bool NeedsManualClaim(TaskRuntimeState state) => state.State == TaskState.ReadyToClaim;
}
