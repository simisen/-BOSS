namespace LeliaFarm.Core.Visuals;

/// <summary>校验结果。</summary>
public sealed class VisualManifestValidation
{
    /// <summary>错误（存在即整份清单不可用）。</summary>
    public List<string> Errors { get; } = new();

    /// <summary>警告（可用，但需要人工注意）。</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>被标记为占位的资源 ID。</summary>
    public List<string> PlaceholderAssetIds { get; } = new();

    /// <summary>是否通过校验。</summary>
    public bool IsValid => this.Errors.Count == 0;
}

/// <summary>
/// 视觉资源清单校验（纯逻辑）。
/// 规则来源：资源路径必须限制在 Mod 内容根目录内，拒绝 <c>..</c> 穿越；
/// 枚类型、版本、数值范围、重复 ID、画布尺寸、帧矩形边界都必须校验。
/// </summary>
public static class VisualManifestValidator
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png"
    };

    private static readonly HashSet<string> MapExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmx", ".tbin", ".xnb"
    };

    private static readonly HashSet<string> SourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ModFile", "ContentAsset"
    };

    /// <summary>校验一份清单。</summary>
    /// <param name="manifest">清单对象。</param>
    /// <param name="fileExists">
    /// 可选：给定相对路径是否存在（由调用方注入，便于测试）。
    /// 为 null 时跳过文件存在性检查（结构校验仍会执行）。
    /// </param>
    public static VisualManifestValidation Validate(VisualManifest manifest, Func<string, bool>? fileExists = null)
    {
        var result = new VisualManifestValidation();

        if (manifest is null)
        {
            result.Errors.Add("视觉清单为空。");
            return result;
        }

        if (manifest.SchemaVersion != VisualManifest.SupportedSchemaVersion)
            result.Errors.Add($"不支持的清单 schemaVersion={manifest.SchemaVersion}（支持 {VisualManifest.SupportedSchemaVersion}）。");

        if (string.IsNullOrWhiteSpace(manifest.ThemeId))
            result.Errors.Add("themeId 不能为空。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        int portraitCount = 0;

        foreach (var asset in manifest.Assets ?? new List<VisualAssetEntry>())
        {
            if (asset is null)
            {
                result.Errors.Add("assets 中存在 null 项。");
                continue;
            }

            string id = asset.Id?.Trim() ?? string.Empty;
            if (id.Length == 0)
            {
                result.Errors.Add("存在缺少 id 的资源条目。");
                continue;
            }

            if (!ids.Add(id))
                result.Errors.Add($"资源 ID 重复：{id}。");

            if (!Enum.TryParse(asset.Kind, ignoreCase: false, out VisualAssetKind kind) || kind == VisualAssetKind.Unknown)
                result.Errors.Add($"资源 {id} 的 kind 未知：'{asset.Kind}'。");

            if (kind == VisualAssetKind.Portrait)
                portraitCount++;

            if (asset.AssetVersion < 1)
                result.Errors.Add($"资源 {id} 的 assetVersion 必须 >= 1（当前 {asset.AssetVersion}）。");

            string sourceType = asset.Source?.Type?.Trim() ?? string.Empty;
            if (!SourceTypes.Contains(sourceType))
                result.Errors.Add($"资源 {id} 的 source.type 非法：'{asset.Source?.Type}'（只支持 ModFile / ContentAsset）。");

            string path = asset.Source?.Path?.Trim() ?? string.Empty;
            if (path.Length == 0)
                result.Errors.Add($"资源 {id} 缺少 source.path。");
            else
                ValidatePath(id, kind, sourceType, path, result, fileExists);

            if (asset.Canvas is null || asset.Canvas.Width <= 0 || asset.Canvas.Height <= 0)
            {
                result.Errors.Add($"资源 {id} 的 canvas 尺寸非法（{asset.Canvas?.Width}x{asset.Canvas?.Height}）。");
            }
            else
            {
                if (asset.Canvas.Width > 8192 || asset.Canvas.Height > 8192)
                    result.Errors.Add($"资源 {id} 的 canvas 超过 8192 上限（{asset.Canvas.Width}x{asset.Canvas.Height}）。");
                ValidateFrames(id, asset, result);
            }

            if (asset.PivotNormalized is not null)
            {
                float px = asset.PivotNormalized.X;
                float py = asset.PivotNormalized.Y;
                if (!IsNormalized(px) || !IsNormalized(py))
                    result.Errors.Add($"资源 {id} 的 pivotNormalized 超出 [0,1]（{px},{py}）。");
            }

            if (asset.Sampling is not null)
            {
                if (!Enum.TryParse(asset.Sampling, ignoreCase: false, out VisualSampling sampling) || sampling == VisualSampling.Unknown)
                    result.Errors.Add($"资源 {id} 的 sampling 未知：'{asset.Sampling}'。");
            }

            if (asset.Provenance is null)
            {
                result.Errors.Add($"资源 {id} 缺少 provenance（placeholder / final / unverified）。");
            }
            else if (!Enum.TryParse(asset.Provenance, ignoreCase: true, out VisualProvenance provenance) || provenance == VisualProvenance.Unknown)
            {
                result.Errors.Add($"资源 {id} 的 provenance 未知：'{asset.Provenance}'。");
            }
            else if (provenance == VisualProvenance.Placeholder)
            {
                result.PlaceholderAssetIds.Add(id);
            }
        }

        if (portraitCount == 0)
            result.Warnings.Add("清单没有 Portrait 资源；HUD 立绘将隐藏（这是允许的降级）。");

        ValidateHud(manifest.Hud, result);
        ValidateEffects(manifest.SceneEffects, result);

        if (result.PlaceholderAssetIds.Count > 0)
            result.Warnings.Add($"有 {result.PlaceholderAssetIds.Count} 个资源标记为占位：{string.Join(", ", result.PlaceholderAssetIds)}。这些不能当作完成品发布。");

        return result;
    }

    private static void ValidatePath(
        string id,
        VisualAssetKind kind,
        string sourceType,
        string path,
        VisualManifestValidation result,
        Func<string, bool>? fileExists)
    {
        // 拒绝越权路径：绝对路径、盘符、UNC、上跳目录、混合分隔符逃逸。
        if (path.Contains("..", StringComparison.Ordinal))
        {
            result.Errors.Add($"资源 {id} 的路径包含 '..' 上跳，已拒绝：{path}。");
            return;
        }

        if (path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal)
            || path.Contains(":", StringComparison.Ordinal))
        {
            result.Errors.Add($"资源 {id} 的路径不是相对路径，已拒绝：{path}。");
            return;
        }

        if (string.Equals(sourceType, "ContentAsset", StringComparison.OrdinalIgnoreCase))
            return; // 游戏资产名（如 Maps/xxx）不做文件性检查

        string ext = System.IO.Path.GetExtension(path);
        var allowed = kind == VisualAssetKind.Map ? MapExtensions : ImageExtensions;
        if (!allowed.Contains(ext))
        {
            result.Errors.Add(kind == VisualAssetKind.Map
                ? $"资源 {id} 是 Map，但扩展名 '{ext}' 不是可运行地图格式（允许 .tmx/.tbin/.xnb）。任意 PNG 不能当作地图。"
                : $"资源 {id} 的扩展名 '{ext}' 不受支持（只支持 .png）。");
            return;
        }

        if (fileExists is not null && !fileExists(path))
            result.Warnings.Add($"资源 {id} 的文件不存在：{path}（将按可选资源降级处理）。");
    }

    private static void ValidateFrames(string id, VisualAssetEntry asset, VisualManifestValidation result)
    {
        var frames = asset.Frames;
        if (frames is null || frames.Count == 0)
            return;

        var names = new HashSet<string>(StringComparer.Ordinal);
        int canvasW = asset.Canvas!.Width;
        int canvasH = asset.Canvas.Height;

        foreach (var frame in frames)
        {
            if (frame is null)
            {
                result.Errors.Add($"资源 {id} 的 frames 中存在 null 项。");
                continue;
            }

            string name = frame.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                result.Errors.Add($"资源 {id} 存在缺少 name 的帧。");
            else if (!names.Add(name))
                result.Errors.Add($"资源 {id} 的帧名重复：{name}。");

            if (frame.Width <= 0 || frame.Height <= 0)
            {
                result.Errors.Add($"资源 {id} 的帧 {name} 尺寸非法（{frame.Width}x{frame.Height}）。");
                continue;
            }

            if (frame.X < 0 || frame.Y < 0 || frame.X + frame.Width > canvasW || frame.Y + frame.Height > canvasH)
            {
                result.Errors.Add(
                    $"资源 {id} 的帧 {name} 越界：矩形 ({frame.X},{frame.Y},{frame.Width}x{frame.Height}) 超出画布 {canvasW}x{canvasH}。");
            }
        }
    }

    private static void ValidateHud(VisualManifestHud? hud, VisualManifestValidation result)
    {
        if (hud is null)
            return;

        if (hud.Portrait is not null)
        {
            var p = hud.Portrait;
            if (!IsFraction(p.MaxWidthFraction))
                result.Errors.Add($"hud.portrait.maxWidthFraction 必须在 (0,1]：{p.MaxWidthFraction}。");
            if (!IsFraction(p.MaxHeightFraction))
                result.Errors.Add($"hud.portrait.maxHeightFraction 必须在 (0,1]：{p.MaxHeightFraction}。");
            if (p.MarginUiPixels < 0f || p.MarginUiPixels > 512f)
                result.Errors.Add($"hud.portrait.marginUiPixels 超出 [0,512]：{p.MarginUiPixels}。");
            if (p.Anchor is not null && !string.Equals(p.Anchor, "BottomLeft", StringComparison.OrdinalIgnoreCase))
            {
                // 首版只支持左下；其他锚点需要先实现再写入文档，避免"配置了但不生效"。
                result.Errors.Add($"hud.portrait.anchor 首版只支持 BottomLeft：'{p.Anchor}'。");
            }
        }

        if (hud.Tasks is not null)
        {
            var t = hud.Tasks;
            if (t.MaxVisible < 0 || t.MaxVisible > 5)
                result.Errors.Add($"hud.tasks.maxVisible 超出 [0,5]：{t.MaxVisible}。");
            if (!IsFraction(t.MaxWidthFraction))
                result.Errors.Add($"hud.tasks.maxWidthFraction 必须在 (0,1]：{t.MaxWidthFraction}。");
        }
    }

    private static void ValidateEffects(VisualManifestSceneEffects? effects, VisualManifestValidation result)
    {
        if (effects?.Fog is null)
            return;

        var fog = effects.Fog;
        if (fog.Opacity < 0f || fog.Opacity > 1f)
            result.Errors.Add($"sceneEffects.fog.opacity 必须在 [0,1]：{fog.Opacity}。");
        if (fog.SpeedWorldPixelsPerSecond < 0f || fog.SpeedWorldPixelsPerSecond > 200f)
            result.Errors.Add($"sceneEffects.fog.speedWorldPixelsPerSecond 超出 [0,200]：{fog.SpeedWorldPixelsPerSecond}。");

        if (effects.TargetFarmOnly is false)
            result.Warnings.Add("sceneEffects.targetFarmOnly=false：雾会作用于所有地点，首版不建议。");
    }

    private static bool IsNormalized(float value) => value is >= 0f and <= 1f;

    private static bool IsFraction(float value) => value is > 0f and <= 1f;
}
