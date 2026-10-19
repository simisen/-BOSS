namespace LeliaFarm.Core.Services;

using LeliaFarm.Core.Configuration;
using LeliaFarm.Core.Diagnostics;
using LeliaFarm.Core.Domain.Tasks;
using LeliaFarm.Core.GameAdapters;
using LeliaFarm.Core.Persistence;
using StardewModdingAPI;
using StardewValley;

/// <summary>HUD 需要的一条任务视图（已本地化的文本 + 进度）。</summary>
public sealed class TaskView
{
    /// <summary>任务 ID。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>标题（已本地化）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>进度文本（已本地化）。</summary>
    public string Progress { get; init; } = string.Empty;

    /// <summary>状态。</summary>
    public TaskState State { get; init; }

    /// <summary>是否已完成（显示为勾）。</summary>
    public bool IsDone => this.State is TaskState.Claimed or TaskState.ReadyToClaim;
}

/// <summary>任务服务接口。只维护进度与领取状态，不负责绘制。</summary>
public interface ITaskService
{
    /// <summary>取 HUD 可见任务（最多 <see cref="MaxVisible"/> 条）。</summary>
    IReadOnlyList<TaskView> GetVisibleTasks(int maxVisible);

    /// <summary>用一帧快照推进任务；返回发生的状态变化。</summary>
    IReadOnlyList<TaskTransition> Refresh(GameSnapshot snapshot);

    /// <summary>取任务运行时状态。</summary>
    TaskRuntimeState? GetState(string taskId);

    /// <summary>取任务定义。</summary>
    TaskDefinition? GetDefinition(string taskId);

    /// <summary>显式领取（自动结算关闭时使用）。</summary>
    bool TryClaim(string taskId, int currentDay, out TaskTransition transition);

    /// <summary>交付型任务定义（没有则 null）。</summary>
    TaskDefinition? DeliverTask { get; }
}

/// <summary>
/// 三项引导任务的实现。状态更新由 SMAPI 事件驱动；
/// 作物格数与抚摸状态用"仅在主题农场、限频"的扫描兜底（不存在可用的种植/抚摸事件）。
/// </summary>
public sealed class TaskService : ITaskService
{
    /// <summary>扫描间隔（tick）。60 tick ≈ 1 秒。</summary>
    public const int ScanIntervalTicks = 30;

    private readonly TaskDefinitionSet definitions;
    private readonly SaveStateStore store;
    private readonly ModDiagnostics diagnostics;
    private readonly ModConfig config;
    private readonly ITranslationHelper translation;

    private bool activatedThisSession;

    /// <summary>构造。</summary>
    public TaskService(
        TaskDefinitionSet definitions,
        SaveStateStore store,
        ModDiagnostics diagnostics,
        ModConfig config,
        ITranslationHelper translation)
    {
        this.definitions = definitions;
        this.store = store;
        this.diagnostics = diagnostics;
        this.config = config;
        this.translation = translation;
    }

    /// <inheritdoc />
    public TaskDefinition? DeliverTask =>
        this.definitions.Tasks.FirstOrDefault(t => t.ObjectiveType == ObjectiveType.DeliverProduce);

    /// <inheritdoc />
    public TaskRuntimeState? GetState(string taskId)
    {
        var root = this.store.Root;
        if (root is null)
            return null;

        return root.Tasks.TryGetValue(taskId, out var state) ? state : null;
    }

    /// <inheritdoc />
    public TaskDefinition? GetDefinition(string taskId) =>
        this.definitions.Tasks.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.Ordinal));

    /// <inheritdoc />
    public IReadOnlyList<TaskView> GetVisibleTasks(int maxVisible)
    {
        if (maxVisible <= 0 || this.store.Root is null)
            return Array.Empty<TaskView>();

        var views = new List<TaskView>(Math.Min(maxVisible, this.definitions.Tasks.Count));

        foreach (var definition in this.definitions.Tasks)
        {
            if (views.Count >= maxVisible)
                break;

            var state = this.GetState(definition.Id);
            if (state is null || state.State == TaskState.Locked)
                continue;

            views.Add(new TaskView
            {
                Id = definition.Id,
                Title = this.translation.Get(definition.TitleKey).ToString(),
                Progress = this.FormatProgress(definition, state),
                State = state.State
            });
        }

        return views;
    }

    /// <inheritdoc />
    public IReadOnlyList<TaskTransition> Refresh(GameSnapshot snapshot)
    {
        var transitions = new List<TaskTransition>();
        var root = this.store.Root;

        if (root is null || !this.config.EnableIntroTasks)
            return transitions;

        if (!snapshot.OnTargetFarm)
        {
            this.activatedThisSession = false;
            return transitions;
        }

        // 只有在场且未终结的任务才需要推进；离开农场不推进、不记录。
        this.ActivateIfNeeded(root, snapshot.TotalDays);

        // 限频由 ModEntry 统一负责（每 ScanIntervalTicks 才构造一次完整快照并调用本方法），
        // 这里不再做二次节流，避免"两处节流、行为难以推理"。

        foreach (var definition in this.definitions.Tasks)
        {
            if (definition.ObjectiveType == ObjectiveType.DeliverProduce)
                continue; // 交付型任务只由交付事务完成

            var state = root.GetOrCreateTask(definition.Id);
            if (state.State is TaskState.Claimed)
                continue;


            int observed = definition.ObjectiveType switch
            {
                ObjectiveType.CurrentCropCount => snapshot.ValidCropCount,
                ObjectiveType.PetAnimalToday => snapshot.AnimalPettedToday ? 1 : 0,
                _ => 0
            };

            var transition = TaskProgressEngine.ApplyObservation(definition, state, observed, snapshot.TotalDays);
            if (transition.Changed)
            {
                transitions.Add(transition);
                this.Grant(definition, transition);
            }
        }

        return transitions;
    }

    /// <inheritdoc />
    public bool TryClaim(string taskId, int currentDay, out TaskTransition transition)
    {
        transition = new TaskTransition { TaskId = taskId };
        var definition = this.GetDefinition(taskId);
        var state = this.GetState(taskId);
        if (definition is null || state is null)
            return false;

        var result = TaskProgressEngine.TryClaim(definition, state, currentDay);
        if (!result.Changed)
            return false;

        transition = result;
        this.Grant(definition, result);
        return true;
    }

    /// <summary>把任务推进到 Claimed（交付成功后调用）。</summary>
    public TaskTransition? CompleteDeliverTask(int currentDay)
    {
        var definition = this.DeliverTask;
        var root = this.store.Root;
        if (definition is null || root is null)
            return null;

        var state = root.GetOrCreateTask(definition.Id);
        var transition = TaskProgressEngine.CompleteAndClaim(definition, state, currentDay);
        if (transition.Changed)
            this.Grant(definition, transition);

        return transition;
    }

    /// <summary>把仍在 Locked 的任务按配置启用。</summary>
    private void ActivateIfNeeded(SaveRoot root, int currentDay)
    {
        bool changed = false;
        foreach (var definition in this.definitions.Tasks)
        {
            var state = root.GetOrCreateTask(definition.Id);
            if (TaskProgressEngine.TryActivate(definition, state))
            {
                state.LastEvaluatedDay = currentDay;
                changed = true;
            }
        }

        if (changed && !this.activatedThisSession)
        {
            this.activatedThisSession = true;
            this.diagnostics.OncePerSave("tasks-activated", LogLevel.Info,
                $"LeliaFarm：已在主题农场启用 {this.definitions.Tasks.Count} 项引导任务。");
        }
    }

    /// <summary>发放奖励（只在该次转换中调用一次）。</summary>
    private void Grant(TaskDefinition definition, TaskTransition transition)
    {
        if (transition.GoldGranted > 0)
        {
            Game1.player.Money += transition.GoldGranted;
            Game1.addHUDMessage(new HUDMessage(
                this.translation.Get("task.reward.gold", new { amount = transition.GoldGranted }).ToString()));
        }

        string title = this.translation.Get(definition.TitleKey).ToString();
        switch (transition.To)
        {
            case TaskState.Claimed:
                Game1.addHUDMessage(new HUDMessage(
                    this.translation.Get("task.completed", new { title }).ToString()));
                break;
            case TaskState.ReadyToClaim:
                Game1.addHUDMessage(new HUDMessage(
                    this.translation.Get("task.readyToClaim", new { title }).ToString()));
                break;
        }

        if (transition.Changed)
        {
            this.diagnostics.Info(
                $"LeliaFarm 任务 {definition.Id}：{transition.From} → {transition.To}（progress={transition.Progress}，gold={transition.GoldGranted}）");
        }
    }

    /// <summary>按目标类型生成与定义一致的进度文案。</summary>
    private string FormatProgress(TaskDefinition definition, TaskRuntimeState state)
    {
        if (state.State is TaskState.Claimed or TaskState.ReadyToClaim)
            return this.translation.Get("task.progress.done").ToString();

        return definition.ObjectiveType switch
        {
            ObjectiveType.CurrentCropCount => this.translation
                .Get("task.progress.crops", new { current = state.Progress, target = TaskProgressEngine.EffectiveTarget(definition) })
                .ToString(),
            ObjectiveType.PetAnimalToday => this.translation.Get("task.progress.pet").ToString(),
            ObjectiveType.DeliverProduce => this.translation.Get("task.progress.deliver").ToString(),
            _ => string.Empty
        };
    }
}
