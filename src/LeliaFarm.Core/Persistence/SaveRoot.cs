namespace LeliaFarm.Core.Persistence;

using System.Text.Json;
using System.Text.Json.Serialization;
using LeliaFarm.Core.Domain.Tasks;

/// <summary>
/// 本 Mod 存档数据的根对象。只包含可序列化的普通数据：
/// 不序列化游戏对象、纹理、委托或渲染状态。
/// </summary>
public sealed class SaveRoot
{
    /// <summary>当前支持的最高 schema 版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>存档数据版本。</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>写入该数据的 Mod 版本，仅用于诊断。</summary>
    [JsonPropertyName("modVersion")]
    public string ModVersion { get; set; } = string.Empty;

    /// <summary>以稳定任务 ID 为键的任务运行时状态。未知 ID 保留而不执行。</summary>
    [JsonPropertyName("tasks")]
    public Dictionary<string, TaskRuntimeState> Tasks { get; set; } = new(StringComparer.Ordinal);

    /// <summary>取某个任务的状态，不存在时创建（不自动写入存档，由调用方决定）。</summary>
    public TaskRuntimeState GetOrCreateTask(string taskId)
    {
        if (!this.Tasks.TryGetValue(taskId, out var state))
        {
            state = TaskRuntimeState.CreateLocked(taskId);
            this.Tasks[taskId] = state;
        }

        return state;
    }
}

/// <summary>存档载入状态。</summary>
public enum SaveLoadStatus
{
    /// <summary>不存在存档数据（新存档）。</summary>
    Empty = 0,

    /// <summary>正常读取。</summary>
    Ok = 1,

    /// <summary>旧版本，已成功迁移。</summary>
    Migrated = 2,

    /// <summary>版本高于本 Mod 支持范围：必须停止写入与奖励发放。</summary>
    TooNew = 3,

    /// <summary>数据损坏或迁移失败：保留原始数据，停止相关写入。</summary>
    Corrupt = 4
}

/// <summary>存档载入结果。</summary>
public sealed class SaveLoadResult
{
    /// <summary>状态。</summary>
    public SaveLoadStatus Status { get; init; }

    /// <summary>成功（或迁移成功）时的数据对象。</summary>
    public SaveRoot? Root { get; init; }

    /// <summary>读到的原始 schema 版本；-1 表示未知。</summary>
    public int FromVersion { get; init; } = -1;

    /// <summary>可读诊断信息。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>原始 JSON，出现 TooNew/Corrupt 时原样保留，绝不覆盖。</summary>
    public string? RawJson { get; init; }

    /// <summary>是否允许继续写入本存档。</summary>
    public bool CanWrite => this.Status is SaveLoadStatus.Empty or SaveLoadStatus.Ok or SaveLoadStatus.Migrated;
}

/// <summary>
/// 存档数据的读取、版本判定与迁移（纯逻辑，可脱机测试）。
/// 迁移是显式的、逐版本的；不做"猜测式"的字段填充。
/// </summary>
public static class SaveMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = false
    };

    /// <summary>把存档对象序列化为 JSON。</summary>
    public static string Serialize(SaveRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.SchemaVersion = SaveRoot.CurrentSchemaVersion;
        return JsonSerializer.Serialize(root, JsonOptions);
    }

    /// <summary>读取并（必要时）迁移存档数据。</summary>
    public static SaveLoadResult Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SaveLoadResult { Status = SaveLoadStatus.Empty, Root = new SaveRoot(), FromVersion = -1 };

        SaveRoot? root;
        int version;
        try
        {
            root = JsonSerializer.Deserialize<SaveRoot>(json, JsonOptions);
            if (root is null)
                return Corrupt(json, "存档数据为 null。");

            // schemaVersion 缺省（旧数据）视为 0。
            version = ReadVersion(json);
        }
        catch (JsonException ex)
        {
            return Corrupt(json, $"存档 JSON 无法解析：{ex.Message}");
        }

        if (version > SaveRoot.CurrentSchemaVersion)
        {
            return new SaveLoadResult
            {
                Status = SaveLoadStatus.TooNew,
                Root = null,
                FromVersion = version,
                RawJson = json,
                Message = $"存档 schemaVersion={version} 高于本 Mod 支持的 {SaveRoot.CurrentSchemaVersion}；"
                        + "已停止写入与奖励发放，原始数据保持不变。"
            };
        }

        // 规范化字典比较器与 ID 字段（反序列化不会保留 Comparer）。
        var tasks = new Dictionary<string, TaskRuntimeState>(StringComparer.Ordinal);
        foreach (var pair in root.Tasks ?? new Dictionary<string, TaskRuntimeState>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                continue;

            pair.Value.Id = pair.Key;
            tasks[pair.Key] = pair.Value;
        }

        root.Tasks = tasks;

        if (version == SaveRoot.CurrentSchemaVersion)
        {
            return new SaveLoadResult
            {
                Status = SaveLoadStatus.Ok,
                Root = root,
                FromVersion = version,
                RawJson = json
            };
        }

        // 逐版本迁移：0 → 1（把缺省 schemaVersion 提升到 1，任务状态已经结构化，无需改写语义）。
        try
        {
            if (version < 1)
            {
                root.SchemaVersion = 1;
                version = 1;
            }

            if (version != SaveRoot.CurrentSchemaVersion)
                return Corrupt(json, $"缺少从 schemaVersion={version} 到 {SaveRoot.CurrentSchemaVersion} 的迁移路径。");

            root.SchemaVersion = SaveRoot.CurrentSchemaVersion;
            return new SaveLoadResult
            {
                Status = SaveLoadStatus.Migrated,
                Root = root,
                FromVersion = ReadVersion(json),
                RawJson = json,
                Message = $"存档数据已从 schemaVersion={ReadVersion(json)} 迁移到 {SaveRoot.CurrentSchemaVersion}。"
            };
        }
        catch (Exception ex)
        {
            return Corrupt(json, $"迁移失败：{ex.Message}");
        }
    }

    /// <summary>从原始 JSON 里只读地取 schemaVersion（缺失视为 0）。</summary>
    public static int ReadVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return -1;

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return -1;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Number
                    && prop.Value.TryGetInt32(out int value))
                {
                    return value;
                }
            }

            return 0;
        }
        catch (JsonException)
        {
            return -1;
        }
    }

    private static SaveLoadResult Corrupt(string json, string message) => new()
    {
        Status = SaveLoadStatus.Corrupt,
        Root = null,
        FromVersion = ReadVersion(json),
        RawJson = json,
        Message = message + " 原始数据已保留，本 Mod 对该存档停止写入。"
    };
}
