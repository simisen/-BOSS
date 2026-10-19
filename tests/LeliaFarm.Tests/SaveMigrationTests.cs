namespace LeliaFarm.Tests;

using LeliaFarm.Core.Domain.Tasks;
using LeliaFarm.Core.Persistence;
using Xunit;

public class SaveMigrationTests
{
    [Fact]
    public void EmptyData_IsEmptyAndWritable()
    {
        var result = SaveMigration.Load(null);

        Assert.Equal(SaveLoadStatus.Empty, result.Status);
        Assert.True(result.CanWrite);
        Assert.NotNull(result.Root);
        Assert.Empty(result.Root!.Tasks);
    }

    [Fact]
    public void RoundTrip_PreservesTaskState()
    {
        var root = new SaveRoot { ModVersion = "0.1.0" };
        var state = root.GetOrCreateTask("intro.plant_5");
        state.State = TaskState.Claimed;
        state.Progress = 5;
        state.ClaimedDay = 12;

        string json = SaveMigration.Serialize(root);
        var loaded = SaveMigration.Load(json);

        Assert.Equal(SaveLoadStatus.Ok, loaded.Status);
        Assert.True(loaded.CanWrite);
        var task = Assert.Single(loaded.Root!.Tasks).Value;
        Assert.Equal(TaskState.Claimed, task.State);
        Assert.Equal(5, task.Progress);
        Assert.Equal(12, task.ClaimedDay);
    }

    [Fact]
    public void OldVersion_IsMigratedToCurrent()
    {
        // schemaVersion 缺失 → 视为 0，应迁移到当前版本。
        const string legacy = "{\"tasks\":{\"intro.pet_animal\":{\"State\":3,\"Progress\":1}}}";

        var result = SaveMigration.Load(legacy);

        Assert.Equal(SaveLoadStatus.Migrated, result.Status);
        Assert.True(result.CanWrite);
        Assert.Equal(0, result.FromVersion);
        Assert.Equal(SaveRoot.CurrentSchemaVersion, result.Root!.SchemaVersion);
        Assert.Equal(TaskState.Claimed, result.Root.Tasks["intro.pet_animal"].State);
    }

    [Fact]
    public void NewerVersion_IsRefusedAndDataKept()
    {
        const string future = "{\"schemaVersion\":999,\"tasks\":{}}";

        var result = SaveMigration.Load(future);

        Assert.Equal(SaveLoadStatus.TooNew, result.Status);
        Assert.False(result.CanWrite);
        Assert.Null(result.Root);
        Assert.Equal(future, result.RawJson); // 原始数据必须原样保留
        Assert.Equal(999, result.FromVersion);
    }

    [Fact]
    public void BrokenJson_IsCorruptAndDataKept()
    {
        const string broken = "{ this is not json";

        var result = SaveMigration.Load(broken);

        Assert.Equal(SaveLoadStatus.Corrupt, result.Status);
        Assert.False(result.CanWrite);
        Assert.Null(result.Root);
        Assert.Equal(broken, result.RawJson);
    }

    [Fact]
    public void UnknownTaskIds_ArePreserved()
    {
        // 未知任务（来自未来版本或已删除的配置）必须原样保留，不被清空、不被执行。
        const string json = "{\"schemaVersion\":1,\"tasks\":{\"future.unknown\":{\"State\":2,\"Progress\":9}}}";

        var result = SaveMigration.Load(json);

        Assert.Equal(SaveLoadStatus.Ok, result.Status);
        Assert.True(result.Root!.Tasks.ContainsKey("future.unknown"));
        Assert.Equal(TaskState.ReadyToClaim, result.Root.Tasks["future.unknown"].State);
    }

    [Fact]
    public void ReadVersion_HandlesMissingAndInvalid()
    {
        Assert.Equal(-1, SaveMigration.ReadVersion(null));
        Assert.Equal(0, SaveMigration.ReadVersion("{\"tasks\":{}}"));
        Assert.Equal(3, SaveMigration.ReadVersion("{\"schemaVersion\":3}"));
        Assert.Equal(-1, SaveMigration.ReadVersion("{ not json"));
    }
}
