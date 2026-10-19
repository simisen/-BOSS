namespace LeliaFarm.Tests;

using LeliaFarm.Core.Domain.Delivery;
using LeliaFarm.Core.Domain.Tasks;
using Xunit;

public class TaskProgressTests
{
    private static TaskDefinition CropTask(int target = 5, bool latch = true, bool autoClaim = true, int gold = 0) => new()
    {
        Id = "intro.plant_5",
        TitleKey = "task.plant5.title",
        ObjectiveType = ObjectiveType.CurrentCropCount,
        Target = target,
        LatchWhenReached = latch,
        AutoClaim = autoClaim,
        Reward = new TaskReward { Gold = gold }
    };

    private static TaskDefinition PetTask(bool autoClaim = true, int gold = 0) => new()
    {
        Id = "intro.pet_animal",
        TitleKey = "task.pet.title",
        ObjectiveType = ObjectiveType.PetAnimalToday,
        Target = 1,
        AutoClaim = autoClaim,
        Reward = new TaskReward { Gold = gold }
    };

    private static TaskDefinition DeliverTask(bool autoClaim = true, int gold = 300) => new()
    {
        Id = "intro.deliver_produce",
        TitleKey = "task.deliver.title",
        ObjectiveType = ObjectiveType.DeliverProduce,
        Target = 1,
        AutoClaim = autoClaim,
        Reward = new TaskReward { Gold = gold },
        AcceptedItemIds = new[] { "(O)24", "(O)188" }
    };

    [Fact]
    public void CropTask_CompletesAtTarget_AndIsLatchedAfterHarvest()
    {
        var def = CropTask(target: 5);
        var state = TaskRuntimeState.CreateLocked(def.Id);
        Assert.True(TaskProgressEngine.TryActivate(def, state));

        var t1 = TaskProgressEngine.ApplyObservation(def, state, observedValue: 4, currentDay: 10);
        Assert.Equal(TaskState.Active, state.State);
        Assert.Equal(4, state.Progress);
        Assert.False(t1.Changed);

        var t2 = TaskProgressEngine.ApplyObservation(def, state, observedValue: 5, currentDay: 10);
        Assert.Equal(TaskState.Claimed, state.State);
        Assert.True(t2.Changed);

        // 收获后作物减少到 2 格：进度不回退（latch），仍是 Claimed。
        var t3 = TaskProgressEngine.ApplyObservation(def, state, observedValue: 2, currentDay: 11);
        Assert.Equal(TaskState.Claimed, state.State);
        Assert.Equal(5, state.Progress);
        Assert.False(t3.Changed);
    }

    [Fact]
    public void CropTask_RepeatedScansDoNotAccumulate()
    {
        var def = CropTask(target: 5);
        var state = TaskRuntimeState.CreateLocked(def.Id);
        TaskProgressEngine.TryActivate(def, state);

        // 5 格作物连续扫描 10 次，进度必须停在 5（不是历史播种次数累加）。
        for (int i = 0; i < 10; i++)
            TaskProgressEngine.ApplyObservation(def, state, observedValue: 5, currentDay: 10);

        Assert.Equal(5, state.Progress);
        Assert.Equal(TaskState.Claimed, state.State);
    }

    [Fact]
    public void CropTask_NonLatched_FollowsObservation()
    {
        var def = CropTask(target: 5, latch: false);
        var state = TaskRuntimeState.CreateLocked(def.Id);
        TaskProgressEngine.TryActivate(def, state);

        TaskProgressEngine.ApplyObservation(def, state, observedValue: 3, currentDay: 1);
        Assert.Equal(3, state.Progress);

        TaskProgressEngine.ApplyObservation(def, state, observedValue: 1, currentDay: 2);
        Assert.Equal(1, state.Progress);
        Assert.Equal(TaskState.Active, state.State);
    }

    [Fact]
    public void LockedTask_DoesNotRecordProgress()
    {
        var def = CropTask();
        var state = TaskRuntimeState.CreateLocked(def.Id);

        var transition = TaskProgressEngine.ApplyObservation(def, state, observedValue: 99, currentDay: 3);

        Assert.Equal(TaskState.Locked, state.State);
        Assert.Equal(0, state.Progress);
        Assert.False(transition.Changed);
    }

    [Fact]
    public void PetTask_CompletesOnceAndRewardIsGrantedOnlyOnce()
    {
        var def = PetTask(gold: 100);
        var state = TaskRuntimeState.CreateLocked(def.Id);
        TaskProgressEngine.TryActivate(def, state);

        var first = TaskProgressEngine.ApplyObservation(def, state, observedValue: 1, currentDay: 5);
        Assert.Equal(TaskState.Claimed, state.State);
        Assert.Equal(100, first.GoldGranted);

        var second = TaskProgressEngine.ApplyObservation(def, state, observedValue: 1, currentDay: 6);
        Assert.Equal(0, second.GoldGranted); // 不重复发放
        Assert.Equal(0, second.GoldGranted);
    }

    [Fact]
    public void AutoClaimDisabled_RequiresExplicitClaim()
    {
        var def = CropTask(target: 1, autoClaim: false, gold: 50);
        var state = TaskRuntimeState.CreateLocked(def.Id);
        TaskProgressEngine.TryActivate(def, state);

        var reached = TaskProgressEngine.ApplyObservation(def, state, observedValue: 1, currentDay: 2);
        Assert.Equal(TaskState.ReadyToClaim, state.State);
        Assert.Equal(0, reached.GoldGranted);

        var claimed = TaskProgressEngine.TryClaim(def, state, currentDay: 3);
        Assert.Equal(TaskState.Claimed, state.State);
        Assert.Equal(50, claimed.GoldGranted);
        Assert.Equal(3, state.ClaimedDay);

        var again = TaskProgressEngine.TryClaim(def, state, currentDay: 4);
        Assert.False(again.Changed);
        Assert.Equal(0, again.GoldGranted);
    }

    [Fact]
    public void DeliverTask_CannotCompleteByObservation()
    {
        var def = DeliverTask();
        var state = TaskRuntimeState.CreateLocked(def.Id);
        TaskProgressEngine.TryActivate(def, state);

        Assert.Throws<InvalidOperationException>(() =>
            TaskProgressEngine.ApplyObservation(def, state, observedValue: 1, currentDay: 1));
    }

    [Fact]
    public void DeliverTask_CompleteAndClaim_IsIdempotent()
    {
        var def = DeliverTask(gold: 300);
        var state = TaskRuntimeState.CreateLocked(def.Id);
        TaskProgressEngine.TryActivate(def, state);

        var first = TaskProgressEngine.CompleteAndClaim(def, state, currentDay: 7);
        Assert.Equal(TaskState.Claimed, state.State);
        Assert.Equal(300, first.GoldGranted);

        var second = TaskProgressEngine.CompleteAndClaim(def, state, currentDay: 7);
        Assert.False(second.Changed);
        Assert.Equal(0, second.GoldGranted);
    }

    [Fact]
    public void DeliveryCheck_RejectsWrongItemEmptyHandAndDuplicates()
    {
        var def = DeliverTask();
        var state = TaskRuntimeState.CreateLocked(def.Id);
        TaskProgressEngine.TryActivate(def, state);

        Assert.Equal(DeliveryCheck.NoItemHeld, DeliveryRules.Check(def, state, null));

        var wrong = new InventorySlotSnapshot("(O)999", 1, 0);
        Assert.Equal(DeliveryCheck.ItemNotAccepted, DeliveryRules.Check(def, state, wrong));

        var right = new InventorySlotSnapshot("(O)24", 1, 0);
        Assert.Equal(DeliveryCheck.Ok, DeliveryRules.Check(def, state, right));

        Assert.Equal(DeliveryCheck.Busy, DeliveryRules.Check(def, state, right, isBusy: true));

        TaskProgressEngine.CompleteAndClaim(def, state, currentDay: 1);
        Assert.Equal(DeliveryCheck.AlreadyClaimed, DeliveryRules.Check(def, state, right));
    }

    [Fact]
    public void DeliveryCheck_RejectsLockedTask()
    {
        var def = DeliverTask();
        var state = TaskRuntimeState.CreateLocked(def.Id);
        var right = new InventorySlotSnapshot("(O)24", 1, 0);

        Assert.Equal(DeliveryCheck.TaskNotActive, DeliveryRules.Check(def, state, right));
    }
}
