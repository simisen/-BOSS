namespace LeliaFarm.Core.Domain.Tasks;

/// <summary>一次状态变化的描述，供 HUD、日志和测试断言使用。</summary>
public sealed class TaskTransition
{
    /// <summary>任务 ID。</summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>变化前状态。</summary>
    public TaskState From { get; init; }

    /// <summary>变化后状态。</summary>
    public TaskState To { get; init; }

    /// <summary>变化后的进度值。</summary>
    public int Progress { get; init; }

    /// <summary>本次结算发放的金钱（没有则为 0）。</summary>
    public int GoldGranted { get; init; }

    /// <summary>是否真的发生了状态变化。</summary>
    public bool Changed => this.From != this.To;
}

/// <summary>
/// 任务进度引擎。纯逻辑、无游戏依赖：所有规则都可脱离游戏进程测试。
/// 不变量：
/// 1) <see cref="TaskState.Claimed"/> 是终态，任何方法都不会让它回退。
/// 2) 奖励只在 Locked/Active/ReadyToClaim → Claimed 的那一次转换中发放，且只发放一次。
/// 3) <see cref="ObjectiveType.DeliverProduce"/> 不能被观测值完成，只能由交付事务完成。
/// </summary>
public static class TaskProgressEngine
{
    /// <summary>布尔型目标（抚摸动物）的有效目标值。</summary>
    public static int EffectiveTarget(TaskDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.ObjectiveType == ObjectiveType.CurrentCropCount
            ? definition.Target
            : Math.Max(1, definition.Target);
    }

    /// <summary>启用任务：Locked → Active。已启用或已终结时返回 false，不做任何修改。</summary>
    public static bool TryActivate(TaskDefinition definition, TaskRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        if (state.State != TaskState.Locked)
            return false;

        state.State = TaskState.Active;
        return true;
    }

    /// <summary>
    /// 应用一次观测值。仅适用于 <see cref="ObjectiveType.CurrentCropCount"/> 与
    /// <see cref="ObjectiveType.PetAnimalToday"/>。达成时按 <see cref="TaskDefinition.AutoClaim"/> 结算。
    /// </summary>
    public static TaskTransition ApplyObservation(TaskDefinition definition, TaskRuntimeState state, int observedValue, int currentDay)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        if (definition.ObjectiveType == ObjectiveType.DeliverProduce)
            throw new InvalidOperationException($"任务 {definition.Id} 是交付型任务，不能被观测值完成。");

        var before = state.State;
        int beforeProgress = state.Progress;

        if (state.State == TaskState.Claimed)
            return Unchanged(definition.Id, state);

        // Locked 的任务保持不动（前置条件未满足时不应记录进度）。
        if (state.State == TaskState.Locked)
            return Unchanged(definition.Id, state);

        int observed = Math.Max(0, observedValue);
        state.Progress = definition.LatchWhenReached
            ? Math.Max(state.Progress, observed)
            : observed;
        state.LastEvaluatedDay = currentDay;

        if (state.State == TaskState.Active && state.Progress >= EffectiveTarget(definition))
            CompleteInternal(definition, state, currentDay);

        return new TaskTransition
        {
            TaskId = definition.Id,
            From = before,
            To = state.State,
            Progress = state.Progress,
            GoldGranted = before != state.State && state.State == TaskState.Claimed ? definition.Reward.Gold : 0
        };
    }

    /// <summary>
    /// 尝试完成并结算（用于交付：交付事务已成功扣物，这里只负责状态与奖励）。
    /// 返回本次发放的金钱；重复调用不会重复发放。
    /// </summary>
    public static TaskTransition CompleteAndClaim(TaskDefinition definition, TaskRuntimeState state, int currentDay)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        var before = state.State;
        if (state.State == TaskState.Claimed)
            return Unchanged(definition.Id, state);

        if (state.State == TaskState.Locked)
            return Unchanged(definition.Id, state);

        CompleteInternal(definition, state, currentDay);

        return new TaskTransition
        {
            TaskId = definition.Id,
            From = before,
            To = state.State,
            Progress = state.Progress,
            GoldGranted = before != state.State && state.State == TaskState.Claimed ? definition.Reward.Gold : 0
        };
    }

    /// <summary>把 ReadyToClaim 结算为 Claimed（自动结算关闭时需要显式领取）。</summary>
    public static TaskTransition TryClaim(TaskDefinition definition, TaskRuntimeState state, int currentDay)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(state);

        if (state.State != TaskState.ReadyToClaim)
            return Unchanged(definition.Id, state);

        var before = state.State;
        state.State = TaskState.Claimed;
        state.ClaimedDay = currentDay;

        return new TaskTransition
        {
            TaskId = definition.Id,
            From = before,
            To = state.State,
            Progress = state.Progress,
            GoldGranted = definition.Reward.Gold
        };
    }

    /// <summary>达成目标后的内部转换；不处理奖励（奖励只由转换点决定）。</summary>
    private static void CompleteInternal(TaskDefinition definition, TaskRuntimeState state, int currentDay)
    {
        if (definition.AutoClaim)
        {
            state.State = TaskState.Claimed;
            state.ClaimedDay = currentDay;
        }
        else
        {
            state.State = TaskState.ReadyToClaim;
        }
    }

    private static TaskTransition Unchanged(string id, TaskRuntimeState state) => new()
    {
        TaskId = id,
        From = state.State,
        To = state.State,
        Progress = state.Progress,
        GoldGranted = 0
    };
}
