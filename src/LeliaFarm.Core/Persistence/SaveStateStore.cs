namespace LeliaFarm.Core.Persistence;

using LeliaFarm.Core.Diagnostics;
using LeliaFarm.Core.Domain.Tasks;
using StardewModdingAPI;

/// <summary>
/// 通过 SMAPI 的存档数据 API 读写本 Mod 自己的数据。
/// 只保存普通数据对象，不保存游戏对象、纹理、委托或渲染状态。
/// 版本高于支持范围或数据损坏时进入只读状态：不再写入、不再发放奖励，原始数据保持不动。
/// </summary>
public sealed class SaveStateStore
{
    /// <summary>存档数据键。</summary>
    public const string DataKey = "save-root";

    private readonly IDataHelper dataHelper;
    private readonly ModDiagnostics diagnostics;
    private readonly string modVersion;

    private SaveRoot? root;
    private SaveLoadStatus status = SaveLoadStatus.Empty;
    private string? lastMessage;
    private bool loaded;

    /// <summary>构造。</summary>
    public SaveStateStore(IDataHelper dataHelper, ModDiagnostics diagnostics, string modVersion)
    {
        this.dataHelper = dataHelper;
        this.diagnostics = diagnostics;
        this.modVersion = modVersion;
    }

    /// <summary>当前数据对象；不可写入时为 null。</summary>
    public SaveRoot? Root => this.CanWrite ? this.root : null;

    /// <summary>载入状态。</summary>
    public SaveLoadStatus Status => this.status;

    /// <summary>是否允许写入（以及发放奖励）。</summary>
    public bool CanWrite => this.status is SaveLoadStatus.Empty or SaveLoadStatus.Ok or SaveLoadStatus.Migrated;

    /// <summary>从当前存档读入数据。可重复调用（每次重新读取）。</summary>
    public void Load()
    {
        this.loaded = true;
        this.root = null;
        this.status = SaveLoadStatus.Empty;
        this.lastMessage = null;

        SaveRoot? read = null;
        try
        {
            read = this.dataHelper.ReadSaveData<SaveRoot>(DataKey);
        }
        catch (Exception ex)
        {
            this.status = SaveLoadStatus.Corrupt;
            this.lastMessage = $"读取存档数据失败：{ex.Message}";
            this.diagnostics.OncePerSave("save-read-failed", LogLevel.Error,
                $"LeliaFarm：{this.lastMessage} 本存档内本 Mod 停止写入。");
            return;
        }

        if (read is null)
        {
            this.status = SaveLoadStatus.Empty;
            this.root = new SaveRoot { ModVersion = this.modVersion };
            return;
        }

        if (read.SchemaVersion > SaveRoot.CurrentSchemaVersion)
        {
            this.status = SaveLoadStatus.TooNew;
            this.lastMessage =
                $"存档 schemaVersion={read.SchemaVersion} 高于本 Mod 支持的 {SaveRoot.CurrentSchemaVersion}。"
                + "已停止写入与奖励发放，原始数据保持不变。";
            this.diagnostics.OncePerSave("save-too-new", LogLevel.Error, $"LeliaFarm：{this.lastMessage}");
            return;
        }

        if (read.SchemaVersion < SaveRoot.CurrentSchemaVersion)
        {
            string from = read.SchemaVersion.ToString();
            try
            {
                read = MigrateInMemory(read);
                this.status = SaveLoadStatus.Migrated;
                this.lastMessage = $"存档数据已从 schemaVersion={from} 迁移到 {SaveRoot.CurrentSchemaVersion}。";
                this.diagnostics.OncePerSave("save-migrated", LogLevel.Info, $"LeliaFarm：{this.lastMessage}");
            }
            catch (Exception ex)
            {
                this.status = SaveLoadStatus.Corrupt;
                this.lastMessage = $"迁移失败：{ex.Message}。原始数据已保留。";
                this.diagnostics.OncePerSave("save-migrate-failed", LogLevel.Error, $"LeliaFarm：{this.lastMessage}");
                return;
            }
        }
        else
        {
            this.status = SaveLoadStatus.Ok;
        }

        read.Tasks ??= new Dictionary<string, TaskRuntimeState>(StringComparer.Ordinal);
        if (read.Tasks.Comparer != StringComparer.Ordinal)
        {
            var normalized = new Dictionary<string, TaskRuntimeState>(StringComparer.Ordinal);
            foreach (var pair in read.Tasks)
                normalized[pair.Key] = pair.Value;
            read.Tasks = normalized;
        }

        this.root = read;
    }

    /// <summary>把当前数据写回存档。只在游戏正常保存流程中调用。</summary>
    public void Save()
    {
        if (!this.loaded)
        {
            this.diagnostics.OncePerSave("save-not-loaded", LogLevel.Warn,
                "LeliaFarm：在未读入存档数据的情况下跳过写入。");
            return;
        }

        if (!this.CanWrite || this.root is null)
        {
            this.diagnostics.OncePerSave("save-skip-write", LogLevel.Warn,
                $"LeliaFarm：当前存档状态为 {this.status}，跳过写入以保护原始数据。");
            return;
        }

        this.root.ModVersion = this.modVersion;
        try
        {
            this.dataHelper.WriteSaveData(DataKey, this.root);
        }
        catch (Exception ex)
        {
            this.diagnostics.OncePerSession("save-write-failed", LogLevel.Error,
                $"LeliaFarm：写入存档数据失败：{ex.Message}");
        }
    }

    /// <summary>切换存档 / 返回标题时清空内存状态。</summary>
    public void Unload()
    {
        this.root = null;
        this.loaded = false;
        this.status = SaveLoadStatus.Empty;
        this.lastMessage = null;
    }

    /// <summary>内存内迁移（当前只有 0 → 1：补齐 schemaVersion 并规范化任务表）。</summary>
    private static SaveRoot MigrateInMemory(SaveRoot source)
    {
        int version = source.SchemaVersion;

        if (version < 1)
        {
            source.SchemaVersion = 1;
            version = 1;
        }

        if (version != SaveRoot.CurrentSchemaVersion)
            throw new InvalidOperationException($"缺少从 schemaVersion={version} 到 {SaveRoot.CurrentSchemaVersion} 的迁移路径。");

        source.SchemaVersion = SaveRoot.CurrentSchemaVersion;
        return source;
    }
}
