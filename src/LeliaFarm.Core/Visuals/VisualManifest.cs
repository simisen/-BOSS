namespace LeliaFarm.Core.Visuals;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>资源类型枚举。未知值必须被拒绝，而不是静默当作未知类型处理。</summary>
public enum VisualAssetKind
{
    /// <summary>未知（非法）。</summary>
    Unknown = 0,

    /// <summary>立绘。</summary>
    Portrait = 1,

    /// <summary>UI 边框 / 主题贴图。</summary>
    UiFrame = 2,

    /// <summary>雾等环境特效贴图。</summary>
    Fog = 3,

    /// <summary>地图（引用经核实的地图资产，不能是任意 PNG）。</summary>
    Map = 4,

    /// <summary>角色外观图集。</summary>
    Appearance = 5,

    /// <summary>图块贴图。</summary>
    TileSheet = 6
}

/// <summary>采样方式。</summary>
public enum VisualSampling
{
    /// <summary>未知（非法）。</summary>
    Unknown = 0,

    /// <summary>硬边采样（像素素材默认）。</summary>
    PointClamp = 1,

    /// <summary>平滑采样（高清立绘）。</summary>
    LinearClamp = 2
}

/// <summary>来源状态。仅用于追踪，不代表自动判定授权。</summary>
public enum VisualProvenance
{
    /// <summary>未知（非法）。</summary>
    Unknown = 0,

    /// <summary>占位素材，不可当作完成品发布。</summary>
    Placeholder = 1,

    /// <summary>正式素材。</summary>
    Final = 2,

    /// <summary>待核实来源。</summary>
    Unverified = 3
}

/// <summary>图集帧矩形。</summary>
public sealed class VisualFrameRect
{
    /// <summary>帧名（同一资源内唯一）。</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>左上角 X（像素）。</summary>
    [JsonPropertyName("x")] public int X { get; set; }

    /// <summary>左上角 Y（像素）。</summary>
    [JsonPropertyName("y")] public int Y { get; set; }

    /// <summary>宽（像素）。</summary>
    [JsonPropertyName("width")] public int Width { get; set; }

    /// <summary>高（像素）。</summary>
    [JsonPropertyName("height")] public int Height { get; set; }
}

/// <summary>归一化锚点。</summary>
public sealed class VisualPivot
{
    /// <summary>X 方向归一化锚点，0 左 1 右。</summary>
    [JsonPropertyName("x")] public float X { get; set; } = 0.5f;

    /// <summary>Y 方向归一化锚点，0 上 1 下。</summary>
    [JsonPropertyName("y")] public float Y { get; set; } = 1f;
}

/// <summary>单条视觉资源条目。</summary>
public sealed class VisualAssetEntry
{
    /// <summary>稳定资源 ID。</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    /// <summary>资源类型。</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;

    /// <summary>来源（类型 + 相对路径）。</summary>
    [JsonPropertyName("source")] public VisualAssetSource Source { get; set; } = new();

    /// <summary>画布尺寸。</summary>
    [JsonPropertyName("canvas")] public VisualCanvas Canvas { get; set; } = new();

    /// <summary>锚点。</summary>
    [JsonPropertyName("pivotNormalized")] public VisualPivot? PivotNormalized { get; set; }

    /// <summary>采样方式。</summary>
    [JsonPropertyName("sampling")] public string? Sampling { get; set; }

    /// <summary>来源状态。</summary>
    [JsonPropertyName("provenance")] public string? Provenance { get; set; }

    /// <summary>资源版本（正整数）。</summary>
    [JsonPropertyName("assetVersion")] public int AssetVersion { get; set; }

    /// <summary>图集帧。</summary>
    [JsonPropertyName("frames")] public List<VisualFrameRect>? Frames { get; set; }
}

/// <summary>资源来源。</summary>
public sealed class VisualAssetSource
{
    /// <summary>来源类型：ModFile 或 ContentAsset。</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "ModFile";

    /// <summary>ModFile 时是相对 Mod 内容根目录的路径；ContentAsset 时是游戏资产名。</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
}

/// <summary>画布尺寸。</summary>
public sealed class VisualCanvas
{
    /// <summary>宽（像素）。</summary>
    [JsonPropertyName("width")] public int Width { get; set; }

    /// <summary>高（像素）。</summary>
    [JsonPropertyName("height")] public int Height { get; set; }
}

/// <summary>HUD 配置段。</summary>
public sealed class VisualManifestHud
{
    /// <summary>立绘配置。</summary>
    [JsonPropertyName("portrait")] public VisualManifestHudPortrait? Portrait { get; set; }

    /// <summary>任务栏配置。</summary>
    [JsonPropertyName("tasks")] public VisualManifestHudTasks? Tasks { get; set; }
}

/// <summary>立绘 HUD 配置。</summary>
public sealed class VisualManifestHudPortrait
{
    /// <summary>锚点名称（首版只支持 BottomLeft）。</summary>
    [JsonPropertyName("anchor")] public string? Anchor { get; set; }

    /// <summary>最大宽度占比。</summary>
    [JsonPropertyName("maxWidthFraction")] public float MaxWidthFraction { get; set; } = 0.18f;

    /// <summary>最大高度占比。</summary>
    [JsonPropertyName("maxHeightFraction")] public float MaxHeightFraction { get; set; } = 0.40f;

    /// <summary>安全边距（UI 像素）。</summary>
    [JsonPropertyName("marginUiPixels")] public float MarginUiPixels { get; set; } = 16f;

    /// <summary>菜单打开时是否隐藏。</summary>
    [JsonPropertyName("hideWhenMenuOpen")] public bool HideWhenMenuOpen { get; set; } = true;
}

/// <summary>任务栏 HUD 配置。</summary>
public sealed class VisualManifestHudTasks
{
    /// <summary>最多显示条数。</summary>
    [JsonPropertyName("maxVisible")] public int MaxVisible { get; set; } = 3;

    /// <summary>最大宽度占比。</summary>
    [JsonPropertyName("maxWidthFraction")] public float MaxWidthFraction { get; set; } = 0.25f;
}

/// <summary>场景特效配置。</summary>
public sealed class VisualManifestSceneEffects
{
    /// <summary>是否仅在目标农场启用。</summary>
    [JsonPropertyName("targetFarmOnly")] public bool TargetFarmOnly { get; set; } = true;

    /// <summary>雾配置。</summary>
    [JsonPropertyName("fog")] public VisualManifestFog? Fog { get; set; }
}

/// <summary>雾配置。</summary>
public sealed class VisualManifestFog
{
    /// <summary>是否启用。</summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    /// <summary>不透明度 0..1。</summary>
    [JsonPropertyName("opacity")] public float Opacity { get; set; } = 0.10f;

    /// <summary>移动速度（世界像素/秒）。</summary>
    [JsonPropertyName("speedWorldPixelsPerSecond")] public float SpeedWorldPixelsPerSecond { get; set; } = 4f;
}

/// <summary>视觉资源清单根对象。</summary>
public sealed class VisualManifest
{
    /// <summary>当前支持的清单 schema 版本。</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>schema 版本。</summary>
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }

    /// <summary>主题 ID。</summary>
    [JsonPropertyName("themeId")] public string ThemeId { get; set; } = string.Empty;

    /// <summary>资源条目。</summary>
    [JsonPropertyName("assets")] public List<VisualAssetEntry> Assets { get; set; } = new();

    /// <summary>HUD 配置。</summary>
    [JsonPropertyName("hud")] public VisualManifestHud? Hud { get; set; }

    /// <summary>场景特效配置。</summary>
    [JsonPropertyName("sceneEffects")] public VisualManifestSceneEffects? SceneEffects { get; set; }

    /// <summary>解析 JSON（失败抛 JsonException，由调用方转成诊断）。</summary>
    public static VisualManifest Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        return JsonSerializer.Deserialize<VisualManifest>(json, options)
               ?? throw new JsonException("视觉清单为 null。");
    }
}
