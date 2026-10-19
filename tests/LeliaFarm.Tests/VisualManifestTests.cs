namespace LeliaFarm.Tests;

using LeliaFarm.Core.Visuals;
using Xunit;

public class VisualManifestTests
{
    private const string ValidJson = """
    {
      "schemaVersion": 1,
      "themeId": "lelia.manor",
      "assets": [
        {
          "id": "portrait.lelia.neutral",
          "kind": "Portrait",
          "source": { "type": "ModFile", "path": "assets/portraits/lelia_neutral.png" },
          "canvas": { "width": 256, "height": 384 },
          "pivotNormalized": { "x": 0.5, "y": 1.0 },
          "sampling": "LinearClamp",
          "provenance": "placeholder",
          "assetVersion": 1,
          "frames": [
            { "name": "idle", "x": 0, "y": 0, "width": 128, "height": 192 }
          ]
        }
      ],
      "hud": {
        "portrait": { "anchor": "BottomLeft", "maxWidthFraction": 0.18, "maxHeightFraction": 0.40 },
        "tasks": { "maxVisible": 3, "maxWidthFraction": 0.25 }
      },
      "sceneEffects": { "targetFarmOnly": true, "fog": { "enabled": true, "opacity": 0.10, "speedWorldPixelsPerSecond": 4 } }
    }
    """;

    [Fact]
    public void ValidManifest_Passes()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        var result = VisualManifestValidator.Validate(manifest);

        Assert.True(result.IsValid, string.Join("\n", result.Errors));
        Assert.Single(result.PlaceholderAssetIds);
    }

    [Fact]
    public void PathTraversal_IsRejected()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        manifest.Assets[0].Source.Path = "../outside/secret.png";

        var result = VisualManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(".."));
    }

    [Fact]
    public void AbsolutePath_IsRejected()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        manifest.Assets[0].Source.Path = "C:/Windows/system32/x.png";

        var result = VisualManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        var clone = new VisualAssetEntry
        {
            Id = manifest.Assets[0].Id,
            Kind = "Portrait",
            Source = new VisualAssetSource { Type = "ModFile", Path = "assets/x.png" },
            Canvas = new VisualCanvas { Width = 16, Height = 16 },
            Provenance = "placeholder",
            AssetVersion = 1
        };
        manifest.Assets.Add(clone);

        var result = VisualManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("重复"));
    }

    [Fact]
    public void FrameOutsideCanvas_IsRejected()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        manifest.Assets[0].Frames![0].Width = 999;

        var result = VisualManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("越界"));
    }

    [Fact]
    public void UnknownKind_IsRejected()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        manifest.Assets[0].Kind = "Sprite";

        var result = VisualManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void MapAssetWithPngExtension_IsRejected()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        manifest.Assets[0].Kind = "Map";
        manifest.Assets[0].Source.Path = "assets/maps/manor.png";

        var result = VisualManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("地图"));
    }

    [Fact]
    public void OutOfRangeHudAndFog_AreRejected()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        manifest.Hud!.Tasks!.MaxVisible = 99;
        manifest.SceneEffects!.Fog!.Opacity = 3f;

        var result = VisualManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2);
    }

    [Fact]
    public void MissingProvenance_IsRejected()
    {
        var manifest = VisualManifest.Parse(ValidJson);
        manifest.Assets[0].Provenance = null;

        var result = VisualManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
    }
}
