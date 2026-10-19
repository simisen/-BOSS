namespace LeliaFarm.Core.Domain.Tasks;

/// <summary>
/// 单个任务在存档中的运行时状态。只包含可序列化的原始数据，
/// 不持有游戏对象、纹理、委托或渲染状态。
/// </summary>
public sealed class TaskRuntimeState
{
    /// <summary>稳定任务 ID（存档键）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>当前状态。</summary>
    public TaskState State { get; set; } = TaskState.Locked;

    /// <summary>
    /// 已达成的观测值。对 <see cref="ObjectiveType.CurrentCropCount"/> 是"观测到的最大有效作物格数"，
    /// 不是历史播种次数；界面文案必须与之一致。
    /// </summary>
    public int Progress { get; set; }

    /// <summary>最近一次评估的游戏内绝对天数（Game1.Date.TotalDays），用于区分"新一天/旧一天"。</summary>
    public int LastEvaluatedDay { get; set; } = -1;

    /// <summary>领取/结算发生的游戏内绝对天数；-1 表示未领取。</summary>
    public int ClaimedDay { get; set; } = -1;

    /// <summary>创建一条默认（Locked）状态。</summary>
    public static TaskRuntimeState CreateLocked(string id) => new() { Id = id, State = TaskState.Locked };

    /// <summary>是否为终态。</summary>
    public bool IsTerminal => this.State == TaskState.Claimed;
}
