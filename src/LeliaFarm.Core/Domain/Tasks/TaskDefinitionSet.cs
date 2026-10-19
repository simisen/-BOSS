namespace LeliaFarm.Core.Domain.Tasks;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>任务定义集合的解析结果（成功或带诊断的失败）。</summary>
public sealed class TaskDefinitionSetResult
{
    /// <summary>解析成功时的定义集合；失败为 null。</summary>
    public TaskDefinitionSet? Set { get; init; }

    /// <summary>诊断信息（每条一行，可直接写日志）。</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>是否解析成功。</summary>
    public bool Ok => this.Set is not null && this.Errors.Count == 0;
}

/// <summary>
/// 任务定义集合。由 JSON 配置驱动，不为单条任务写专用代码。
/// 未知 <c>objectiveType</c>、未知 <c>scope</c>、重复 ID 一律拒绝整份配置。
/// </summary>
public sealed class TaskDefinitionSet
{
    /// <summary>当前支持的 schemaVersion。</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>配置 schema 版本。</summary>
    public int SchemaVersion { get; init; }

    /// <summary>任务定义（保持配置顺序）。</summary>
    public IReadOnlyList<TaskDefinition> Tasks { get; init; } = Array.Empty<TaskDefinition>();

    /// <summary>按稳定 ID 建立索引。</summary>
    public IReadOnlyDictionary<string, TaskDefinition> ById =>
        this.Tasks.ToDictionary(t => t.Id, StringComparer.Ordinal);

    /// <summary>解析并校验一份任务配置 JSON。任何错误都返回失败，不返回"部分可用"的结果。</summary>
    public static TaskDefinitionSetResult Parse(string json)
    {
        TaskDefinitionSetDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<TaskDefinitionSetDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new TaskDefinitionSetResult { Errors = new[] { $"tasks.json 不是合法 JSON：{ex.Message}" } };
        }

        if (dto is null)
            return new TaskDefinitionSetResult { Errors = new[] { "tasks.json 内容为空。" } };

        var errors = new List<string>();

        if (dto.SchemaVersion != SupportedSchemaVersion)
            errors.Add($"不支持的 schemaVersion={dto.SchemaVersion}（本版本支持 {SupportedSchemaVersion}）。");

        if (dto.Tasks is null || dto.Tasks.Count == 0)
            errors.Add("tasks 为空，至少需要一条任务定义。");

        var tasks = new List<TaskDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in dto.Tasks ?? new List<TaskDefinitionDto>())
        {
            if (raw is null)
            {
                errors.Add("tasks 中存在 null 项。");
                continue;
            }

            string id = raw.Id?.Trim() ?? string.Empty;
            if (id.Length == 0)
            {
                errors.Add("存在缺少 id 的任务定义。");
                continue;
            }

            if (!seen.Add(id))
            {
                errors.Add($"任务 id 重复：{id}。");
                continue;
            }

            if (!TryParseObjectiveType(raw.ObjectiveType, out ObjectiveType objectiveType))
            {
                errors.Add($"任务 {id} 的 objectiveType 未知：'{raw.ObjectiveType}'。首版只支持 CurrentCropCount / PetAnimalToday / DeliverProduce。");
                continue;
            }

            string scope = raw.Scope?.Trim() ?? TaskScopes.TargetFarm;
            if (!string.Equals(scope, TaskScopes.TargetFarm, StringComparison.Ordinal))
            {
                errors.Add($"任务 {id} 的 scope 未知：'{raw.Scope}'。首版只支持 TargetFarm。");
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw.TitleKey))
            {
                errors.Add($"任务 {id} 缺少 titleKey。");
                continue;
            }

            if (objectiveType == ObjectiveType.CurrentCropCount && raw.Target <= 0)
            {
                errors.Add($"任务 {id} 需要正整数 target（当前 {raw.Target}）。");
                continue;
            }

            var accepted = raw.AcceptedItemIds ?? new List<string>();
            if (objectiveType == ObjectiveType.DeliverProduce && accepted.Count == 0)
            {
                errors.Add($"任务 {id} 是 DeliverProduce，但 acceptedItemIds 为空。");
                continue;
            }

            int badIdIndex = accepted.FindIndex(x => string.IsNullOrWhiteSpace(x));
            if (badIdIndex >= 0)
            {
                errors.Add($"任务 {id} 的 acceptedItemIds 第 {badIdIndex} 项为空。");
                continue;
            }

            if (raw.Reward is not null && raw.Reward.Gold < 0)
            {
                errors.Add($"任务 {id} 的 reward.gold 为负（{raw.Reward.Gold}）。");
                continue;
            }

            tasks.Add(new TaskDefinition
            {
                Id = id,
                TitleKey = raw.TitleKey!.Trim(),
                ObjectiveType = objectiveType,
                Target = raw.Target,
                Scope = scope,
                LatchWhenReached = raw.LatchWhenReached ?? true,
                AutoClaim = raw.AutoClaim ?? true,
                Reward = new TaskReward { Gold = raw.Reward?.Gold ?? 0 },
                AcceptedItemIds = accepted.Select(x => x.Trim()).ToArray()
            });
        }

        if (errors.Count > 0)
            return new TaskDefinitionSetResult { Errors = errors };

        return new TaskDefinitionSetResult
        {
            Set = new TaskDefinitionSet { SchemaVersion = dto.SchemaVersion, Tasks = tasks }
        };
    }

    private static bool TryParseObjectiveType(string? value, out ObjectiveType type)
    {
        type = ObjectiveType.None;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // 只接受枚举名本身，避免 "1" 之类被静默接受。
        foreach (ObjectiveType candidate in Enum.GetValues<ObjectiveType>())
        {
            if (candidate == ObjectiveType.None)
                continue;
            if (string.Equals(candidate.ToString(), value.Trim(), StringComparison.Ordinal))
            {
                type = candidate;
                return true;
            }
        }

        return false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // ---- DTO ----

    private sealed class TaskDefinitionSetDto
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }

        [JsonPropertyName("tasks")] public List<TaskDefinitionDto>? Tasks { get; set; }
    }

    private sealed class TaskDefinitionDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }

        [JsonPropertyName("titleKey")] public string? TitleKey { get; set; }

        [JsonPropertyName("objectiveType")] public string? ObjectiveType { get; set; }

        [JsonPropertyName("target")] public int Target { get; set; }

        [JsonPropertyName("scope")] public string? Scope { get; set; }

        [JsonPropertyName("latchWhenReached")] public bool? LatchWhenReached { get; set; }

        [JsonPropertyName("autoClaim")] public bool? AutoClaim { get; set; }

        [JsonPropertyName("reward")] public TaskRewardDto? Reward { get; set; }

        [JsonPropertyName("acceptedItemIds")] public List<string>? AcceptedItemIds { get; set; }
    }

    private sealed class TaskRewardDto
    {
        [JsonPropertyName("gold")] public int Gold { get; set; }
    }
}
