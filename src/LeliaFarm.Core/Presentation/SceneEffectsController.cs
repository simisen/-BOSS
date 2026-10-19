namespace LeliaFarm.Core.Presentation;

using LeliaFarm.Core.Visuals;
using LeliaFarm.Core.Configuration;
using LeliaFarm.Core.Diagnostics;
using LeliaFarm.Core.GameAdapters;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

/// <summary>
/// 主题农场的环境表现：一层低透明雾 + 少量暖光。
/// 只在目标农场激活；离开地图立刻停止绘制并清理自身状态；不改变全局天气。
/// 说明：当前雾绘制在"世界上层"，不做地形遮挡（如实记录，不声称实现了复杂遮挡）。
/// </summary>
public sealed class SceneEffectsController
{
    private const string FogAssetId = "effects.fog.soft";
    private const int MaxFogTiles = 64;

    private readonly IModHelper helper;
    private readonly ModConfig config;
    private readonly IVisualAssetCatalog catalog;
    private readonly ModDiagnostics diagnostics;
    private readonly GameStateReader reader;

    private Texture2D? fogTexture;
    private double elapsedSeconds;
    private bool registered;

    /// <summary>构造。</summary>
    public SceneEffectsController(
        IModHelper helper,
        ModConfig config,
        IVisualAssetCatalog catalog,
        ModDiagnostics diagnostics,
        GameStateReader reader)
    {
        this.helper = helper;
        this.config = config;
        this.catalog = catalog;
        this.diagnostics = diagnostics;
        this.reader = reader;
    }

    /// <summary>注册事件（整个进程只注册一次）。</summary>
    public void Register()
    {
        if (this.registered)
        {
            this.diagnostics.OncePerSession("effects-already-registered", LogLevel.Trace,
                "LeliaFarm：场景特效事件已注册，忽略重复注册。");
            return;
        }

        this.helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        this.helper.Events.Display.Rendered += this.OnRendered;
        this.registered = true;
    }

    /// <summary>清理与存档相关的临时状态（返回标题 / 退出时调用）。</summary>
    public void Reset()
    {
        this.elapsedSeconds = 0d;
        this.fogTexture = null;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.currentGameTime is null)
            return;

        // 暂停 / 菜单打开时不推进时间，避免"暂停了雾还在动"。
        if (Game1.paused || Game1.activeClickableMenu is not null)
            return;

        this.elapsedSeconds += Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
    }

    private void OnRendered(object? sender, RenderedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.config.EnableFog)
            return;

        var snapshot = this.reader.ReadLightweight();
        if (!snapshot.OnTargetFarm)
            return;

        var fogSettings = this.catalog.Manifest?.SceneEffects?.Fog;
        if (fogSettings is null || !fogSettings.Enabled)
            return;

        if (this.catalog.Manifest?.SceneEffects?.TargetFarmOnly == false)
        {
            this.diagnostics.OncePerSession("fog-global", LogLevel.Warn,
                "LeliaFarm：视觉清单声明 targetFarmOnly=false，但本 Mod 仍只在主题农场绘制雾。");
        }

        this.fogTexture ??= this.catalog.GetTexture(FogAssetId);
        if (this.fogTexture is null)
        {
            this.diagnostics.OncePerSession("fog-missing", LogLevel.Warn,
                "LeliaFarm：缺少雾贴图 effects.fog.soft，雾效关闭（其余玩法不受影响）。");
            return;
        }

        this.DrawFog(e.SpriteBatch, fogSettings.Opacity, fogSettings.SpeedWorldPixelsPerSecond);
    }

    private void DrawFog(SpriteBatch batch, float opacity, float speed)
    {
        var vp = Game1.viewport;
        int size = this.fogTexture!.Width;
        if (size <= 0)
            return;

        byte alpha = (byte)Math.Clamp((int)(Math.Clamp(opacity, 0f, 1f) * 255f), 0, 255);
        if (alpha == 0)
            return;

        // 世界坐标滚动：速度单位为世界像素/秒。
        double offset = (this.elapsedSeconds * speed) % size;
        int startX = vp.X - (int)offset;
        int startY = vp.Y - (int)(offset * 0.5);

        int tilesX = (vp.Width / size) + 2;
        int tilesY = (vp.Height / size) + 2;

        int drawn = 0;
        var color = new Color(Color.White, alpha);

        for (int y = 0; y < tilesY && drawn < MaxFogTiles; y++)
        {
            for (int x = 0; x < tilesX && drawn < MaxFogTiles; x++)
            {
                int px = startX + (x * size);
                int py = startY + (y * size);
                batch.Draw(this.fogTexture, new Rectangle(px, py, size, size), color);
                drawn++;
            }
        }
    }
}
