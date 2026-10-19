namespace LeliaFarm.Core.Diagnostics;

using StardewModdingAPI;

/// <summary>
/// 诊断输出。同一类问题按 key 去重，避免每帧刷屏；
/// 与存档相关的 key 在切换存档时重置，保证新存档仍能看到一次诊断。
/// </summary>
public sealed class ModDiagnostics
{
    private readonly IMonitor monitor;
    private readonly HashSet<string> sessionKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> saveKeys = new(StringComparer.Ordinal);

    /// <summary>构造。</summary>
    public ModDiagnostics(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    /// <summary>普通信息（每次都输出）。</summary>
    public void Info(string message) => this.monitor.Log(message, LogLevel.Info);

    /// <summary>警告（每次都输出）。</summary>
    public void Warn(string message) => this.monitor.Log(message, LogLevel.Warn);

    /// <summary>错误（每次都输出）。</summary>
    public void Error(string message) => this.monitor.Log(message, LogLevel.Error);

    /// <summary>整个游戏进程内只输出一次。</summary>
    public void OncePerSession(string key, LogLevel level, string message)
    {
        if (this.sessionKeys.Add(key))
            this.monitor.Log(message, level);
    }

    /// <summary>同一个存档内只输出一次。</summary>
    public void OncePerSave(string key, LogLevel level, string message)
    {
        if (this.saveKeys.Add(key))
            this.monitor.Log(message, level);
    }

    /// <summary>切换存档时清空"按存档去重"的记录。</summary>
    public void ResetSaveScope() => this.saveKeys.Clear();
}
