namespace LeliaFarm.Tests;

using LeliaFarm.Core.Domain.Tasks;
using Xunit;

public class TaskDefinitionSetTests
{
    private const string Valid = """
    {
      "schemaVersion": 1,
      "tasks": [
        { "id": "intro.plant_5", "titleKey": "task.plant5.title", "objectiveType": "CurrentCropCount", "target": 5 },
        { "id": "intro.pet_animal", "titleKey": "task.pet.title", "objectiveType": "PetAnimalToday", "target": 1 },
        {
          "id": "intro.deliver_produce", "titleKey": "task.deliver.title", "objectiveType": "DeliverProduce",
          "target": 1, "reward": { "gold": 300 }, "acceptedItemIds": [ "(O)24" ]
        }
      ]
    }
    """;

    [Fact]
    public void ValidDefinitions_AreParsed()
    {
        var result = TaskDefinitionSet.Parse(Valid);

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(3, result.Set!.Tasks.Count);
        Assert.Equal(ObjectiveType.DeliverProduce, result.Set.ById["intro.deliver_produce"].ObjectiveType);
        Assert.Equal(300, result.Set.ById["intro.deliver_produce"].Reward.Gold);
    }

    [Fact]
    public void UnknownObjectiveType_IsRejected()
    {
        const string json = """
        { "schemaVersion": 1, "tasks": [ { "id": "x", "titleKey": "t", "objectiveType": "CraftItem", "target": 1 } ] }
        """;

        var result = TaskDefinitionSet.Parse(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("objectiveType"));
    }

    [Fact]
    public void NumericObjectiveType_IsRejected()
    {
        // 只接受枚举名，"1" 不能被静默当作 CurrentCropCount。
        const string json = """
        { "schemaVersion": 1, "tasks": [ { "id": "x", "titleKey": "t", "objectiveType": "1", "target": 1 } ] }
        """;

        var result = TaskDefinitionSet.Parse(json);

        Assert.False(result.Ok);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "tasks": [
            { "id": "a", "titleKey": "t", "objectiveType": "PetAnimalToday", "target": 1 },
            { "id": "a", "titleKey": "t", "objectiveType": "PetAnimalToday", "target": 1 }
          ]
        }
        """;

        var result = TaskDefinitionSet.Parse(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("重复"));
    }

    [Fact]
    public void UnsupportedSchemaVersion_IsRejected()
    {
        const string json = """
        { "schemaVersion": 7, "tasks": [ { "id": "a", "titleKey": "t", "objectiveType": "PetAnimalToday", "target": 1 } ] }
        """;

        var result = TaskDefinitionSet.Parse(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("schemaVersion"));
    }

    [Fact]
    public void DeliverTaskWithoutItems_IsRejected()
    {
        const string json = """
        { "schemaVersion": 1, "tasks": [ { "id": "a", "titleKey": "t", "objectiveType": "DeliverProduce", "target": 1 } ] }
        """;

        var result = TaskDefinitionSet.Parse(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("acceptedItemIds"));
    }

    [Fact]
    public void UnknownScope_IsRejected()
    {
        const string json = """
        { "schemaVersion": 1, "tasks": [ { "id": "a", "titleKey": "t", "objectiveType": "PetAnimalToday", "target": 1, "scope": "WholeWorld" } ] }
        """;

        var result = TaskDefinitionSet.Parse(json);

        Assert.False(result.Ok);
    }

    [Fact]
    public void ShippedTaskFile_IsValid()
    {
        // 直接校验实际随包发布的 assets/tasks.json，避免"示例合法、实物非法"。
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "tasks.json");
        Assert.True(File.Exists(path), $"缺少随包文件：{path}");

        var result = TaskDefinitionSet.Parse(File.ReadAllText(path));

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(3, result.Set!.Tasks.Count);
        Assert.Contains(result.Set.Tasks, t => t.Id == "intro.plant_5" && t.Target == 5);
        Assert.Contains(result.Set.Tasks, t => t.Id == "intro.pet_animal");
        Assert.Contains(result.Set.Tasks, t => t.Id == "intro.deliver_produce" && t.AcceptedItemIds.Count > 0);
    }
}
