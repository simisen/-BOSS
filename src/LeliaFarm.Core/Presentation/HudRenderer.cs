namespace LeliaFarm.Core.Presentation;

using LeliaFarm.Core.Visuals;
using LeliaFarm.Core.Configuration;
using LeliaFarm.Core.Diagnostics;
using LeliaFarm.Core.Domain.Hud;
using LeliaFarm.Core.GameAdapters;
using LeliaFarm.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

/// <summary>
/// 占位 HUD：体力条 + 最多三项任务 + 可替换立绘占位。
/// 只在目标农场、正常游玩时显示；菜单/对话/过场/加载时隐藏。
/// 渲染回调只绘制已有状态，不做文件 IO、不做任务判定。
/// </summary>
public sealed class HudRenderer
{
    private readonly IModHelper helper;
    private readonly ModConfig config;
    private readonly TaskService tasks;
    private readonly IVisualAssetCatalog catalog;
    private readonly ModDiagnostics diagnostics;
    private readonly ITranslationHelper translation;
    private readonly GameStateReader reader;

    private Texture2D? pixel;
    private bool hudHiddenByKey;

    /// <summary>构造。</summary>
    public HudRenderer(
        IModHelper helper,
        ModConfig config,
        TaskService tasks,
        IVisualAssetCatalog catalog,
        ModDiagnostics diagnostics,
        ITranslationHelper translation,
        GameStateReader reader)
    {
        this.helper = helper;
        this.config = config;
        this.tasks = tasks;
        this.catalog = catalog;
        this.diagnostics = diagnostics;
        this.translation = translation;
        this.reader = reader;
    }

    /// <summary>注册渲染与输入事件。</summary>
    public void Register()
    {
        this.pixel = this.TryLoadPixel();
        this.helper.Events.Display.RenderedHud += this.OnRenderedHud;
        this.helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }

    /// <summary>注销事件（返回标题 / 退出时调用，避免重复绑定）。</summary>
    public void Unregister()
    {
        this.helper.Events.Display.RenderedHud -= this.OnRenderedHud;
        this.helper.Events.Input.ButtonPressed -= this.OnButtonPressed;
        this.hudHiddenByKey = false;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.Button != this.config.ToggleHudKey)
            return;

        this.hudHiddenByKey = !this.hudHiddenByKey;
        Game1.addHUDMessage(new HUDMessage(
            this.translation.Get(this.hudHiddenByKey ? "hud.hidden" : "hud.shown").ToString()));
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        try
        {
            this.Draw(e.SpriteBatch);
        }
        catch (Exception ex)
        {
            // 渲染异常不能让整个 HUD 每帧刷屏。
            this.diagnostics.OncePerSave("hud-draw-error", LogLevel.Error,
                $"LeliaFarm：HUD 绘制异常（已停止本次绘制）：{ex.Message}");
        }
    }

    private void Draw(SpriteBatch batch)
    {
        if (!Context.IsWorldReady || !this.config.EnableHud || this.hudHiddenByKey)
            return;

        GameSnapshot snapshot = this.reader.ReadLightweight();
        if (!snapshot.OnTargetFarm || snapshot.IsMenuOpen)
            return;

        if (this.pixel is null)
            return;

        float viewportW = Game1.uiViewport.Width;
        float viewportH = Game1.uiViewport.Height;
        if (viewportW <= 1 || viewportH <= 1)
            return;

        float toolbarTop = GameStateReader.GetToolbarTop();
        float toolbarHeight = Math.Max(0f, viewportH - toolbarTop);

        var layoutConfig = new HudLayoutConfig
        {
            MaxVisibleTasks = 3,
            UserScale = this.config.HudScale,
            PortraitAspect = this.GetPortraitAspect(),
            MarginUiPixels = 16f,
            PortraitMaxWidthFraction = 0.18f,
            PortraitMaxHeightFraction = 0.40f,
            TasksMaxWidthFraction = 0.25f
        };

        var layout = HudLayoutCalculator.Compute(
            new HudLayoutInput
            {
                ViewportWidth = viewportW,
                ViewportHeight = viewportH,
                ToolbarHeight = toolbarHeight,
                TopRightReservedHeight = GameStateReader.GetTopRightReservedHeight()
            },
            layoutConfig);

        this.DrawPortrait(batch, layout, snapshot);
        this.DrawTasks(batch, layout, snapshot);
    }

    private void DrawPortrait(SpriteBatch batch, HudLayoutResult layout, GameSnapshot snapshot)
    {
        if (!this.config.ShowPlaceholderPortrait || layout.Portrait is null)
            return;

        var rect = layout.Portrait.Value;

        // 立绘只读游戏状态选择表情；不修改真实体力。
        string assetId = this.SelectPortraitAsset(snapshot.StaminaRatio);
        Texture2D? portrait = this.catalog.GetTexture(assetId);

        if (portrait is null)
        {
            // 可选资源缺失：画一个占位框，而不是报错或崩掉。
            batch.Draw(this.pixel, new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height),
                new Color(40, 44, 40, 160));
            return;
        }

        batch.Draw(portrait, new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height), Color.White);
        this.DrawStaminaBar(batch, rect, snapshot.StaminaRatio);
    }

    private void DrawStaminaBar(SpriteBatch batch, LayoutRect portraitRect, float ratio)
    {
        float width = portraitRect.Width;
        float height = Math.Max(4f, 6f * this.config.HudScale);
        float x = portraitRect.X;
        float y = portraitRect.Y - height - 4f;

        batch.Draw(this.pixel, new Rectangle((int)x, (int)y, (int)width, (int)height), new Color(0, 0, 0, 150));

        int filled = (int)(width * Math.Clamp(ratio, 0f, 1f));
        if (filled > 0)
            batch.Draw(this.pixel, new Rectangle((int)x, (int)y, filled, (int)height), new Color(196, 210, 180));
    }

    private void DrawTasks(SpriteBatch batch, HudLayoutResult layout, GameSnapshot snapshot)
    {
        if (layout.TaskPanel is null || layout.VisibleTaskCount <= 0)
            return;

        var panel = layout.TaskPanel.Value;
        batch.Draw(this.pixel, new Rectangle((int)panel.X, (int)panel.Y, (int)panel.Width, (int)panel.Height),
            new Color(18, 20, 18, 150));

        var views = this.tasks.GetVisibleTasks(layout.VisibleTaskCount);
        if (views.Count == 0)
            return;

        float scale = Math.Max(0.6f, this.config.HudScale);
        float lineHeight = 30f * scale;
        float y = panel.Y + 6f * scale;

        foreach (var view in views)
        {
            if (y + lineHeight > panel.Bottom)
                break;

            string text = view.IsDone
                ? $"✔ {view.Title}"
                : $"{view.Title}  {view.Progress}";

            batch.DrawString(Game1.smallFont, text, new Vector2(panel.X + 8f * scale, y),
                view.IsDone ? new Color(150, 190, 150) : Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            y += lineHeight;
        }
    }

    /// <summary>按体力比例选择立绘，优先级：tired &lt; working &lt; happy &lt; neutral。</summary>
    private string SelectPortraitAsset(float staminaRatio)
    {
        if (staminaRatio <= 0.20f)
            return "portrait.lelia.tired";

        return this.tasks.GetVisibleTasks(3).Count > 0 && staminaRatio >= 0.90f
            ? "portrait.lelia.working"
            : "portrait.lelia.neutral";
    }

    private float GetPortraitAspect()
    {
        var entry = this.catalog.Manifest?.Assets.FirstOrDefault(a =>
            string.Equals(a.Kind, "Portrait", StringComparison.Ordinal) && a.Canvas is { Height: > 0 });

        if (entry?.Canvas is { Width: > 0, Height: > 0 })
            return (float)entry.Canvas.Width / entry.Canvas.Height;

        return 2f / 3f;
    }

    private Texture2D? TryLoadPixel()
    {
        try
        {
            return this.helper.ModContent.Load<Texture2D>("assets/ui/hud_panel.png");
        }
        catch (Exception ex)
        {
            this.diagnostics.OncePerSession("hud-pixel-missing", LogLevel.Warn,
                $"LeliaFarm：缺少 HUD 底色贴图 assets/ui/hud_panel.png（{ex.Message}），HUD 将不绘制。");
            return null;
        }
    }
}
