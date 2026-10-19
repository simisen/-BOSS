namespace LeliaFarm.Core.Delivery;

/// <summary>玩家手持物品的纯数据快照。</summary>
public sealed class DeliveryItemSnapshot
{
    /// <summary>限定物品 ID（如 <c>(O)24</c>）。</summary>
    public string QualifiedItemId { get; init; } = string.Empty;

    /// <summary>显示名，仅用于提示文案。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>数量。</summary>
    public int Stack { get; init; }

    /// <summary>品质。</summary>
    public int Quality { get; init; }
}
