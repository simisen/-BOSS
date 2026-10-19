namespace LeliaFarm.Core.Domain.Tasks;

/// <summary>任务奖励。首版只支持金钱奖励，避免引入未验证的物品发放路径。</summary>
public sealed class TaskReward
{
    /// <summary>奖励金钱。0 表示无奖励。</summary>
    public int Gold { get; init; }
}

/// <summary>
/// 一条任务定义。来源于可配置 JSON（contracts/tasks.example.json），
/// 不允许为单条任务写专用 UI 或专用代码路径。
/// </summary>
public sealed class TaskDefinition
{
    /// <summary>稳定任务 ID，写入存档的键。发布后不可改名。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>标题的 i18n 键。</summary>
    public string TitleKey { get; init; } = string.Empty;

    /// <summary>目标类型。</summary>
    public ObjectiveType ObjectiveType { get; init; }

    /// <summary>目标数值（<see cref="ObjectiveType.CurrentCropCount"/> 使用）。</summary>
    public int Target { get; init; }

    /// <summary>作用域。首版只接受 <c>TargetFarm</c>。</summary>
    public string Scope { get; init; } = TaskScopes.TargetFarm;

    /// <summary>达成后是否锁定进度（不作为历史计数）。</summary>
    public bool LatchWhenReached { get; init; } = true;

    /// <summary>达成目标后是否自动结算为 Claimed（不需要玩家点领取）。</summary>
    public bool AutoClaim { get; init; } = true;

    /// <summary>奖励。</summary>
    public TaskReward Reward { get; init; } = new();

    /// <summary>交付型任务允许提交的物品限定 ID / 分类（仅 <see cref="ObjectiveType.DeliverProduce"/> 使用）。</summary>
    public IReadOnlyList<string> AcceptedItemIds { get; init; } = Array.Empty<string>();
}

/// <summary>任务作用域取值。</summary>
public static class TaskScopes
{
    /// <summary>只统计目标农场（主题农场）。</summary>
    public const string TargetFarm = "TargetFarm";
}
