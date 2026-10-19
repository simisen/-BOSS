namespace LeliaFarm.Core.GameAdapters;

using LeliaFarm.Core.Delivery;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;

/// <summary>
/// <see cref="IGameStateReader"/> 的游戏实现。
/// 只在游戏线程调用；不做文件 IO，不分配长期对象。
/// </summary>
public sealed class GameStateReader : IGameStateReader
{
    private readonly ModConstantsProvider constants;

    /// <summary>构造。</summary>
    public GameStateReader(ModConstantsProvider constants)
    {
        this.constants = constants;
    }

    /// <summary>当前存档的农场类型 ID 是否为主题农场。</summary>
    public static bool IsTargetFarmType() =>
        string.Equals(Game1.GetFarmTypeID(), ModConstants.FarmTypeId, StringComparison.OrdinalIgnoreCase);

    /// <summary>读取一帧快照。</summary>
    public GameSnapshot Read()
    {
        if (!Context.IsWorldReady)
        {
            return new GameSnapshot
            {
                OnTargetFarm = false,
                TotalDays = -1,
                IsMenuOpen = true
            };
        }

        bool onTargetFarm = Game1.currentLocation is Farm && IsTargetFarmType();
        bool menuOpen = Game1.activeClickableMenu is not null;

        return new GameSnapshot
        {
            OnTargetFarm = onTargetFarm,
            FarmTypeId = Game1.GetFarmTypeID(),
            TotalDays = Game1.Date.TotalDays,
            ValidCropCount = onTargetFarm ? CountValidCrops() : 0,
            AnimalPettedToday = onTargetFarm && AnyAnimalPettedToday(),
            StaminaRatio = ReadStaminaRatio(),
            IsMenuOpen = menuOpen,
            HeldItem = ReadHeldItem()
        };
    }

    /// <summary>轻量快照：不做农场统计，供渲染回调每帧调用。</summary>
    public GameSnapshot ReadLightweight()
    {
        if (!Context.IsWorldReady)
        {
            return new GameSnapshot
            {
                OnTargetFarm = false,
                TotalDays = -1,
                IsMenuOpen = true
            };
        }

        bool onTargetFarm = Game1.currentLocation is Farm && IsTargetFarmType();

        return new GameSnapshot
        {
            OnTargetFarm = onTargetFarm,
            FarmTypeId = Game1.GetFarmTypeID(),
            TotalDays = Game1.Date.TotalDays,
            ValidCropCount = 0,
            AnimalPettedToday = false,
            StaminaRatio = ReadStaminaRatio(),
            IsMenuOpen = Game1.activeClickableMenu is not null,
            HeldItem = null
        };
    }

    /// <summary>统计主题农场内"当前有效作物"的格数（非死亡、已种植）。</summary>
    public static int CountValidCrops()
    {
        Farm farm = Game1.getFarm();
        int count = 0;
        foreach (var feature in farm.terrainFeatures.Values)
        {
            if (feature is HoeDirt dirt && dirt.crop is not null && !dirt.crop.dead.Value)
                count++;
        }

        return count;
    }

    /// <summary>主题农场内今日是否有动物被抚摸（原版 <c>FarmAnimal.wasPet</c>）。</summary>
    public static bool AnyAnimalPettedToday()
    {
        foreach (FarmAnimal animal in Game1.getFarm().getAllFarmAnimals())
        {
            if (animal.wasPet.Value)
                return true;
        }

        return false;
    }

    /// <summary>读取体力比例。</summary>
    public static float ReadStaminaRatio()
    {
        var player = Game1.player;
        if (player is null || player.MaxStamina <= 0)
            return 1f;

        return Math.Clamp(player.Stamina / player.MaxStamina, 0f, 1f);
    }

    /// <summary>读取手持物品快照。</summary>
    public static DeliveryItemSnapshot? ReadHeldItem()
    {
        var item = Game1.player?.CurrentItem;
        if (item is null)
            return null;

        return new DeliveryItemSnapshot
        {
            QualifiedItemId = item.QualifiedItemId,
            DisplayName = item.DisplayName,
            Stack = item.Stack,
            Quality = item.Quality
        };
    }

    /// <summary>原版底部工具栏的顶边 Y（UI 像素）。找不到时用保守估计值。</summary>
    public static float GetToolbarTop()
    {
        foreach (IClickableMenu menu in Game1.onScreenMenus)
        {
            if (menu is Toolbar toolbar)
                return toolbar.yPositionOnScreen;
        }

        return Math.Max(0, Game1.uiViewport.Height - 96);
    }

    /// <summary>右上角原版时钟与钱币区域的高度估计（UI 像素），任务栏必须避开。</summary>
    public static float GetTopRightReservedHeight() => Math.Max(0, 72f);
}

/// <summary>把 <see cref="ModConstants"/> 以实例形式注入，避免各处硬编码字符串。</summary>
public sealed class ModConstantsProvider
{
    /// <summary>主题农场类型 ID。</summary>
    public string FarmTypeId => ModConstants.FarmTypeId;
}
