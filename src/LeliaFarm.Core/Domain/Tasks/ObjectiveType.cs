namespace LeliaFarm.Core.Domain.Tasks;

/// <summary>
/// 任务目标类型。首版只支持 <see cref="CurrentCropCount"/>、<see cref="PetAnimalToday"/>、
/// <see cref="DeliverProduce"/>；未列出的类型必须被拒绝，不允许"猜测执行"。
/// </summary>
public enum ObjectiveType
{
    /// <summary>未设置（用于反序列化失败时的显式非法值）。</summary>
    None = 0,

    /// <summary>主题农场内"当前有效作物"的格数达到 target。</summary>
    CurrentCropCount = 1,

    /// <summary>主题农场内有原版动物在今日被抚摸。</summary>
    PetAnimalToday = 2,

    /// <summary>在指定交付入口提交一件符合配置的农产品。</summary>
    DeliverProduce = 3
}

/// <summary>任务状态机。</summary>
public enum TaskState
{
    /// <summary>未启用（前置条件未满足，或不适用当前存档）。</summary>
    Locked = 0,

    /// <summary>进行中。</summary>
    Active = 1,

    /// <summary>目标已达成，等待玩家领取。</summary>
    ReadyToClaim = 2,

    /// <summary>已领取 / 已结算。终态，不会回退。</summary>
    Claimed = 3
}
