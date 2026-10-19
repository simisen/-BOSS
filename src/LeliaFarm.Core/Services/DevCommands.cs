namespace LeliaFarm.Core.Services;

using LeliaFarm.Core.Configuration;
using LeliaFarm.Core.Diagnostics;
using LeliaFarm.Core.Domain.Tasks;
using LeliaFarm.Core.GameAdapters;
using LeliaFarm.Core.Persistence;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.TerrainFeatures;

/// <summary>
/// 开发样片命令。默认关闭（<see cref="ModConfig.EnableDevCommands"/>），
/// 只在专用测试存档里手动执行。幂等：重复执行不会堆叠建筑/动物，也不会清空用户农场。
/// </summary>
public sealed class DevCommands
{
    private readonly IModHelper helper;
    private readonly ModConfig config;
    private readonly ModDiagnostics diagnostics;
    private readonly TaskService tasks;
    private readonly SaveStateStore store;

    /// <summary>构造。</summary>
    public DevCommands(IModHelper helper, ModConfig config, ModDiagnostics diagnostics, TaskService tasks, SaveStateStore store)
    {
        this.helper = helper;
        this.config = config;
        this.diagnostics = diagnostics;
        this.tasks = tasks;
        this.store = store;
    }

    /// <summary>注册控制台命令。</summary>
    public void Register()
    {
        this.helper.ConsoleCommands.Add(
            "lelia_dev_setup",
            "在主题农场生成开发样片（鸡舍/畜舍、鸡与猪、5 格作物）。幂等；默认需要 EnableDevCommands=true。",
            this.HandleSetup);

        this.helper.ConsoleCommands.Add(
            "lelia_dev_status",
            "打印 LeliaFarm 的任务与存档状态。",
            this.HandleStatus);

        this.helper.ConsoleCommands.Add(
            "lelia_dev_visuals",
            "重新校验并重载视觉清单（失败时保留旧配置）。",
            this.HandleVisuals);
    }

    // ---- 命令 ----

    private void HandleSetup(string command, string[] args)
    {
        if (!this.EnsureUsable("lelia_dev_setup"))
            return;

        var farm = Game1.getFarm();
        this.diagnostics.Info("lelia_dev_setup：开始生成开发样片（幂等，不删除既有对象）。");

        int chickens = this.EnsureAnimals(farm, "White Chicken", 2);
        int pigs = this.EnsureAnimals(farm, "Pig", 1);
        this.diagnostics.Info($"lelia_dev_setup：动物 +{chickens} 只鸡 / +{pigs} 头猪（已存在的不重复生成）。");

        int buildings = this.EnsureBuildings(farm);
        this.diagnostics.Info($"lelia_dev_setup：建筑 +{buildings} 座（已存在的不重复生成）。");

        int crops = this.EnsureCrops(farm, 5);
        this.diagnostics.Info($"lelia_dev_setup：作物 +{crops} 格（已存在作物的地块不覆盖）。");

        this.diagnostics.Info("lelia_dev_setup：完成。注意：本命令只应在专用测试存档使用。");
    }

    private void HandleStatus(string command, string[] args)
    {
        this.diagnostics.Info($"LeliaFarm 存档状态：{this.store.Status}，可写入={this.store.CanWrite}。");
        this.diagnostics.Info($"当前农场类型：'{Game1.GetFarmTypeID()}'，目标={ModConstants.FarmTypeId}，匹配={GameStateReader.IsTargetFarmType()}。");

        if (this.store.Root is null)
        {
            this.diagnostics.Info("没有可用的任务状态（存档未载入或不可写入）。");
            return;
        }

        foreach (var pair in this.store.Root.Tasks)
        {
            this.diagnostics.Info(
                $"任务 {pair.Key}：state={pair.Value.State} progress={pair.Value.Progress} claimedDay={pair.Value.ClaimedDay}");
        }
    }

    private void HandleVisuals(string command, string[] args)
    {
        var catalog = new Visuals.VisualAssetCatalog(this.helper, this.diagnostics);
        bool ok = catalog.TryReload(out var messages);
        foreach (string message in messages)
            this.diagnostics.Info("视觉清单：" + message);

        this.diagnostics.Info(ok
            ? "视觉清单重载成功。"
            : "视觉清单重载失败，已保留旧配置（详情见上方诊断）。");
    }

    // ---- 幂等生成 ----

    private bool EnsureUsable(string commandName)
    {
        if (!this.config.EnableDevCommands)
        {
            this.diagnostics.Warn($"{commandName}：开发命令已关闭。请在 config.json 设置 EnableDevCommands=true 后重进游戏。");
            return false;
        }

        if (!Context.IsWorldReady || Game1.player is null)
        {
            this.diagnostics.Warn($"{commandName}：需要先进入一个存档。");
            return false;
        }

        if (!GameStateReader.IsTargetFarmType())
        {
            this.diagnostics.Warn($"{commandName}：当前存档不是主题农场（'{Game1.GetFarmTypeID()}'），已停止，避免污染其他农场。");
            return false;
        }

        if (!this.store.CanWrite)
        {
            this.diagnostics.Warn($"{commandName}：存档数据状态为 {this.store.Status}，已停止写入相关操作。");
            return false;
        }

        return true;
    }

    private int EnsureAnimals(Farm farm, string animalType, int targetCount)
    {
        int existing = farm.getAllFarmAnimals().Count(a => string.Equals(a.type.Value, animalType, StringComparison.Ordinal));
        if (existing >= targetCount)
        {
            this.diagnostics.Info($"lelia_dev_setup：{animalType} 已有 {existing} 只，跳过。");
            return 0;
        }

        int added = 0;
        for (int i = existing; i < targetCount; i++)
        {
            try
            {
                long id = 1 + (farm.animals.Keys.Any() ? farm.animals.Keys.Max() : 0);
                var animal = new FarmAnimal(animalType, id, Game1.player.UniqueMultiplayerID)
                {
                    Name = $"{animalType} {id}"
                };

                if (Utility.addAnimalToFarm(animal))
                    added++;
                else
                    this.diagnostics.Warn($"lelia_dev_setup：{animalType} 添加失败（可能没有空闲畜舍位置）。");
            }
            catch (Exception ex)
            {
                this.diagnostics.Error($"lelia_dev_setup：添加 {animalType} 异常：{ex.Message}");
            }
        }

        return added;
    }

    private int EnsureBuildings(Farm farm)
    {
        int added = 0;
        added += this.EnsureBuilding(farm, "Coop", ModConstants.DeliveryTileX - 18, ModConstants.DeliveryTileY - 8);
        added += this.EnsureBuilding(farm, "Barn", ModConstants.DeliveryTileX - 8, ModConstants.DeliveryTileY - 8);
        return added;
    }

    private int EnsureBuilding(Farm farm, string buildingType, int tileX, int tileY)
    {
        bool exists = farm.buildings.Any(b =>
            string.Equals(b.buildingType.Value, buildingType, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            this.diagnostics.Info($"lelia_dev_setup：已存在 {buildingType}，跳过（不移动、不删除既有建筑）。");
            return 0;
        }

        if (tileX < 0 || tileY < 0)
        {
            this.diagnostics.Warn($"lelia_dev_setup：{buildingType} 目标坐标非法（{tileX},{tileY}），跳过。");
            return 0;
        }

        if (!farm.isTileOpenBesidesTerrainFeatures(new Vector2(tileX, tileY)))
        {
            this.diagnostics.Warn($"lelia_dev_setup：{buildingType} 目标地块 ({tileX},{tileY}) 被占用，跳过该部分。");
            return 0;
        }

        try
        {
            var building = new Building(buildingType, new Vector2(tileX, tileY));
            building.daysOfConstructionLeft.Value = 0;
            farm.buildings.Add(building);
            return 1;
        }
        catch (Exception ex)
        {
            this.diagnostics.Error($"lelia_dev_setup：创建 {buildingType} 异常：{ex.Message}");
            return 0;
        }
    }

    private int EnsureCrops(Farm farm, int targetCount)
    {
        int planted = 0;
        int placed = 0;

        for (int y = ModConstants.DeliveryTileY; y < ModConstants.DeliveryTileY + 6 && placed < targetCount; y++)
        {
            for (int x = ModConstants.DeliveryTileX - 6; x < ModConstants.DeliveryTileX + 4 && placed < targetCount; x++)
            {
                var tile = new Vector2(x, y);
                if (farm.terrainFeatures.ContainsKey(tile))
                    continue;

                if (!farm.isTileOpenBesidesTerrainFeatures(tile))
                    continue;

                try
                {
                    var dirt = new HoeDirt(0, farm)
                    {
                        crop = new Crop("472", x, y, farm)
                    };
                    farm.terrainFeatures[tile] = dirt;
                    placed++;
                    planted++;
                }
                catch (Exception ex)
                {
                    this.diagnostics.Error($"lelia_dev_setup：在 ({x},{y}) 种植异常：{ex.Message}");
                    break;
                }
            }
        }

        if (planted == 0)
            this.diagnostics.Info("lelia_dev_setup：作物区没有可种植的空地，已跳过。");

        return planted;
    }
}
