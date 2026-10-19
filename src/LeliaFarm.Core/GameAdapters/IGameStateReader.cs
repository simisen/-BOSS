namespace LeliaFarm.Core.GameAdapters;

/// <summary>
/// 交给纯领域逻辑的一帧只读快照。刻意做成普通 DTO：
/// 领域层拿不到 <c>Game1</c>、纹理或 Location 对象，因此可以脱机测试。
/// </summary>
public sealed class GameSnapshot
{
    /// <summary>当前是否站在主题农场上。</summary>
    public bool OnTargetFarm { get; init; }

    /// <summary>当前存档的农场类型 ID（<c>Game1.GetFarmTypeID()</c>）；不在农场时为 null。</summary>
    public string? FarmTypeId { get; init; }

    /// <summary>游戏内绝对天数（<c>Game1.Date.TotalDays</c>）。</summary>
    public int TotalDays { get; init; }

    /// <summary>主题农场内当前有效作物格数（非目标农场时为 0）。</summary>
    public int ValidCropCount { get; init; }

    /// <summary>主题农场内今日是否有动物被抚摸。</summary>
    public bool AnimalPettedToday { get; init; }

    /// <summary>体力比例 0..1。</summary>
    public float StaminaRatio { get; init; }

    /// <summary>是否打开了任何菜单 / 对话 / 过场。</summary>
    public bool IsMenuOpen { get; init; }

    /// <summary>玩家当前手持物品快照；空手为 null。</summary>
    public Delivery.DeliveryItemSnapshot? HeldItem { get; init; }
}

/// <summary>
/// 只读游戏状态出口。实现者负责把游戏对象翻译成普通快照；
/// 纯领域逻辑只依赖这个接口，不接触全局游戏对象。
/// </summary>
public interface IGameStateReader
{
    /// <summary>
    /// 读取完整快照（含农场作物/动物统计）。开销较大，调用方必须限频，
    /// 例如每 30 tick 一次，而不是每帧。
    /// </summary>
    GameSnapshot Read();

    /// <summary>
    /// 读取轻量快照：不统计作物与动物，供每帧渲染（HUD、雾效）使用。
    /// 渲染回调里禁止做全农场遍历。
    /// </summary>
    GameSnapshot ReadLightweight();
}
