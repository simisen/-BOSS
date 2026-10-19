namespace LeliaFarm.Tests;

using System.Text.Json;
using System.Xml.Linq;
using Xunit;

/// <summary>
/// 内容包与地图的结构校验。没有游戏进程时，这是对"地图资产是否可运行"能达到的最强验证：
/// 校验图层、尺寸、图块属性、tilesheet 资产名，以及 content.json 引用的文件是否真实存在。
/// </summary>
public class MapAndContentPackTests
{
    private static string PackRoot => Path.Combine(AppContext.BaseDirectory, "ContentPack");

    private static XDocument LoadMap() =>
        XDocument.Load(Path.Combine(PackRoot, "maps", "manor_farm.tmx"));

    [Fact]
    public void Map_HasRequiredFarmLayersAndSize()
    {
        var doc = LoadMap();
        var map = doc.Root!;

        Assert.Equal("64", map.Attribute("width")!.Value);
        Assert.Equal("40", map.Attribute("height")!.Value);
        Assert.Equal("16", map.Attribute("tilewidth")!.Value);
        Assert.Equal("16", map.Attribute("tileheight")!.Value);

        var layerNames = map.Elements("layer").Select(l => (string)l.Attribute("name")!).ToArray();
        Assert.Contains("Back", layerNames);
        Assert.Contains("Buildings", layerNames);
        Assert.Contains("Paths", layerNames);
        Assert.Contains("Front", layerNames);

        foreach (var layer in map.Elements("layer"))
        {
            var data = layer.Element("data")!;
            Assert.Equal("csv", (string?)data.Attribute("encoding"));
            var rows = data.Value.Trim().Split('\n');
            Assert.Equal(40, rows.Length);
            Assert.Equal(64, rows[0].Split(',').Length);
        }
    }

    [Fact]
    public void Map_TileSheetPointsAtContentAsset()
    {
        var tileset = LoadMap().Root!.Element("tileset")!;
        var image = tileset.Element("image")!;

        // 运行时只有"内容资产名"能被游戏内容管理器解析；写相对文件路径会加载失败。
        Assert.Equal("Maps/lelia_farm_tiles", (string)image.Attribute("source")!);
        Assert.Equal("1", (string)tileset.Attribute("firstgid")!);
        Assert.Equal("64", (string)image.Attribute("width")!);
        Assert.Equal("16", (string)image.Attribute("height")!);
    }

    [Fact]
    public void Map_SoilTileCarriesDirtProperty()
    {
        // 耕种依赖 Back 图层图块上的 Type=Dirt 属性；缺了它锄头无法翻地。
        var tileset = LoadMap().Root!.Element("tileset")!;
        var dirt = tileset.Elements("tile").FirstOrDefault(t => (string?)t.Attribute("id") == "2");

        Assert.NotNull(dirt);
        var typeProp = dirt!.Element("properties")!.Elements("property")
            .FirstOrDefault(p => (string?)p.Attribute("name") == "Type");

        Assert.NotNull(typeProp);
        Assert.Equal("Dirt", (string)typeProp!.Attribute("value")!);
    }

    [Fact]
    public void Map_OutdoorsPropertyIsSet()
    {
        var props = LoadMap().Root!.Element("properties")!.Elements("property").ToList();
        var outdoors = props.FirstOrDefault(p => (string?)p.Attribute("name") == "Outdoors");

        Assert.NotNull(outdoors);
        Assert.Equal("T", (string)outdoors!.Attribute("value")!);
    }

    [Fact]
    public void Map_DeliveryTileIsOnPathLayer()
    {
        // 交付入口 (42,18) 必须位于石板路（gid 4）上，与 Core 的常量一致。
        var paths = LoadMap().Root!.Elements("layer")
            .First(l => (string?)l.Attribute("name") == "Paths")
            .Element("data")!.Value.Trim().Split('\n');

        var row = paths[18].Split(',');
        Assert.Equal("4", row[42].Trim());
    }

    [Fact]
    public void ContentPack_ReferencesExistingFilesAndFarmType()
    {
        string contentJson = File.ReadAllText(Path.Combine(PackRoot, "content.json"));
        using var doc = JsonDocument.Parse(contentJson);
        var changes = doc.RootElement.GetProperty("Changes");

        var loadedTargets = new List<string>();
        string? farmEntryId = null;
        string? mapName = null;

        foreach (var change in changes.EnumerateArray())
        {
            string action = change.GetProperty("Action").GetString()!;
            switch (action)
            {
                case "Load":
                {
                    loadedTargets.Add(change.GetProperty("Target").GetString()!);
                    string fromFile = change.GetProperty("FromFile").GetString()!;
                    string full = Path.Combine(PackRoot, fromFile.Replace('/', Path.DirectorySeparatorChar));
                    Assert.True(File.Exists(full), $"content.json 引用了不存在的文件：{fromFile}");
                    break;
                }

                case "EditData":
                {
                    Assert.Equal("Data/AdditionalFarms", change.GetProperty("Target").GetString());
                    var entries = change.GetProperty("Entries");
                    var entry = entries.EnumerateObject().First();
                    farmEntryId = entry.Name;
                    mapName = entry.Value.GetProperty("MapName").GetString();
                    break;
                }
            }
        }

        Assert.Equal("LeliaFarm.Content_ManorFarm", farmEntryId);
        Assert.Equal("Maps/LeliaFarm_Manor", mapName);
        Assert.Contains("Maps/LeliaFarm_Manor", loadedTargets);
        Assert.Contains("Maps/lelia_farm_tiles", loadedTargets);
    }

    [Fact]
    public void ContentPack_ManifestDeclaresContentPatcher()
    {
        string manifestJson = File.ReadAllText(Path.Combine(PackRoot, "manifest.json"));
        using var doc = JsonDocument.Parse(manifestJson);

        Assert.Equal("LeliaFarm.Content", doc.RootElement.GetProperty("UniqueID").GetString());
        Assert.Equal("Pathoschild.ContentPatcher",
            doc.RootElement.GetProperty("ContentPackFor").GetProperty("UniqueID").GetString());

        var deps = doc.RootElement.GetProperty("Dependencies").EnumerateArray()
            .Select(d => d.GetProperty("UniqueID").GetString()).ToArray();
        Assert.Contains("LeliaFarm.Core", deps);
    }

    [Fact]
    public void ShippedVisualManifest_IsValid()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "visual-manifest.json");
        Assert.True(File.Exists(path), $"缺少随包文件：{path}");

        var manifest = LeliaFarm.Core.Visuals.VisualManifest.Parse(File.ReadAllText(path));
        var result = LeliaFarm.Core.Visuals.VisualManifestValidator.Validate(manifest);

        Assert.True(result.IsValid, string.Join("\n", result.Errors));
    }
}
