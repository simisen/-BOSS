namespace LeliaFarm.Core.Visuals;

using LeliaFarm.Core.Diagnostics;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

/// <summary>按稳定资源 ID 解析、验证、缓存与失效视觉资源。</summary>
public interface IVisualAssetCatalog
{
    /// <summary>清单是否通过校验（false 时只使用占位/隐藏）。</summary>
    bool IsValid { get; }

    /// <summary>清单对象（可能为 null）。</summary>
    VisualManifest? Manifest { get; }

    /// <summary>按 ID 取纹理；缺失或非法返回 null（调用方负责降级）。</summary>
    Texture2D? GetTexture(string assetId);

    /// <summary>重新校验并重载（开发命令使用：先验证候选，成功后整体切换，失败保留旧配置）。</summary>
    bool TryReload(out IReadOnlyList<string> messages);
}

/// <summary>
/// 视觉资源目录实现。
/// 约定：路径限制在 Mod 内容根目录内；不在每帧读文件；不 Dispose 游戏内容管理器共享的纹理。
/// </summary>
public sealed class VisualAssetCatalog : IVisualAssetCatalog
{
    private readonly IModHelper helper;
    private readonly ModDiagnostics diagnostics;
    private readonly string manifestRelativePath;

    private readonly Dictionary<string, Texture2D?> textureCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> missingLogged = new(StringComparer.Ordinal);

    private VisualManifest? manifest;
    private Dictionary<string, VisualAssetEntry> assetsById = new(StringComparer.Ordinal);

    /// <summary>构造。</summary>
    public VisualAssetCatalog(IModHelper helper, ModDiagnostics diagnostics, string manifestRelativePath = "assets/visual-manifest.json")
    {
        this.helper = helper;
        this.diagnostics = diagnostics;
        this.manifestRelativePath = manifestRelativePath;
    }

    /// <inheritdoc />
    public bool IsValid { get; private set; }

    /// <inheritdoc />
    public VisualManifest? Manifest => this.manifest;

    /// <summary>首次载入（在 Entry 中调用一次）。</summary>
    public void Initialize()
    {
        this.TryReload(out _);
    }

    /// <inheritdoc />
    public bool TryReload(out IReadOnlyList<string> messages)
    {
        var log = new List<string>();

        string fullPath = Path.Combine(this.helper.DirectoryPath, this.manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            this.IsValid = false;
            this.manifest = null;
            this.assetsById = new Dictionary<string, VisualAssetEntry>(StringComparer.Ordinal);
            this.textureCache.Clear();
            this.diagnostics.OncePerSession("visual-manifest-missing", LogLevel.Warn,
                $"LeliaFarm：缺少视觉清单 {this.manifestRelativePath}，HUD 使用内置占位、雾效关闭。");
            messages = log;
            return false;
        }

        VisualManifest candidate;
        try
        {
            candidate = VisualManifest.Parse(File.ReadAllText(fullPath));
        }
        catch (Exception ex)
        {
            this.diagnostics.OncePerSession("visual-manifest-parse", LogLevel.Error,
                $"LeliaFarm：视觉清单无法解析（{ex.Message}），保留旧配置。");
            messages = log;
            return false;
        }

        string root = this.helper.DirectoryPath;
        var validation = VisualManifestValidator.Validate(candidate, relative =>
            File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))));

        foreach (string warning in validation.Warnings)
        {
            log.Add(warning);
            this.diagnostics.OncePerSession("visual-warn:" + warning, LogLevel.Debug, "LeliaFarm 视觉清单警告：" + warning);
        }

        if (!validation.IsValid)
        {
            foreach (string error in validation.Errors)
            {
                log.Add(error);
                this.diagnostics.OncePerSession("visual-error:" + error, LogLevel.Error, "LeliaFarm 视觉清单错误：" + error);
            }

            this.diagnostics.OncePerSession("visual-invalid-keep", LogLevel.Warn,
                "LeliaFarm：候选视觉清单未通过校验，已保留上一份可用配置。");
            messages = log;
            return false;
        }

        // 校验通过后整体切换，避免出现"一半新一半旧"的状态。
        this.manifest = candidate;
        this.assetsById = candidate.Assets.ToDictionary(a => a.Id, StringComparer.Ordinal);
        this.textureCache.Clear();
        this.missingLogged.Clear();
        this.IsValid = true;

        messages = log;
        this.diagnostics.OncePerSession("visual-loaded", LogLevel.Info,
            $"LeliaFarm：视觉清单已加载（theme={candidate.ThemeId}，{candidate.Assets.Count} 个资源，"
            + $"{validation.PlaceholderAssetIds.Count} 个占位）。");
        return true;
    }

    /// <inheritdoc />
    public Texture2D? GetTexture(string assetId)
    {
        if (this.textureCache.TryGetValue(assetId, out Texture2D? cached))
            return cached;

        Texture2D? texture = null;

        if (this.manifest is not null && this.assetsById.TryGetValue(assetId, out var entry))
        {
            texture = this.LoadTexture(entry);
        }
        else if (this.missingLogged.Add(assetId))
        {
            this.diagnostics.OncePerSession("visual-missing-id:" + assetId, LogLevel.Debug,
                $"LeliaFarm：视觉清单里没有资源 {assetId}，按可选资源降级。");
        }

        this.textureCache[assetId] = texture;
        return texture;
    }

    private Texture2D? LoadTexture(VisualAssetEntry entry)
    {
        try
        {
            if (string.Equals(entry.Source.Type, "ContentAsset", StringComparison.OrdinalIgnoreCase))
                return this.helper.GameContent.Load<Texture2D>(entry.Source.Path);

            // ModFile：由 SMAPI 的 Mod 内容管理器加载，路径已在清单校验中确认没有越权。
            return this.helper.ModContent.Load<Texture2D>(entry.Source.Path);
        }
        catch (Exception ex)
        {
            if (this.missingLogged.Add("load:" + entry.Id))
            {
                this.diagnostics.OncePerSession("visual-load-failed:" + entry.Id, LogLevel.Warn,
                    $"LeliaFarm：资源 {entry.Id} 加载失败（{ex.Message}），按可选资源降级。");
            }

            return null;
        }
    }
}
