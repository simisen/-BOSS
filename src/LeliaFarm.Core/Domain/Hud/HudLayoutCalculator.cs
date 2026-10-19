namespace LeliaFarm.Core.Domain.Hud;

/// <summary>
/// 轴对齐矩形（UI 像素）。自己定义而不复用 XNA 的 Rectangle，是为了让布局计算
/// 完全脱离游戏程序集，可以在单元测试里精确断言。
/// </summary>
public readonly struct LayoutRect
{
    /// <summary>构造矩形。</summary>
    public LayoutRect(float x, float y, float width, float height)
    {
        this.X = x;
        this.Y = y;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>左上角 X。</summary>
    public float X { get; }

    /// <summary>左上角 Y。</summary>
    public float Y { get; }

    /// <summary>宽。</summary>
    public float Width { get; }

    /// <summary>高。</summary>
    public float Height { get; }

    /// <summary>右边界。</summary>
    public float Right => this.X + this.Width;

    /// <summary>下边界。</summary>
    public float Bottom => this.Y + this.Height;

    /// <summary>是否面积为正。</summary>
    public bool IsEmpty => this.Width <= 0f || this.Height <= 0f;

    /// <summary>是否完全落在给定视口内（含边界）。</summary>
    public bool IsInside(float viewportWidth, float viewportHeight)
    {
        return this.X >= -0.01f && this.Y >= -0.01f
            && this.Right <= viewportWidth + 0.01f
            && this.Bottom <= viewportHeight + 0.01f;
    }

    /// <summary>与另一个矩形是否相交（面积重叠才算）。</summary>
    public bool IntersectsWith(LayoutRect other)
    {
        if (this.IsEmpty || other.IsEmpty)
            return false;

        return this.X < other.Right && other.X < this.Right
            && this.Y < other.Bottom && other.Y < this.Bottom;
    }

    /// <inheritdoc />
    public override string ToString() => $"({this.X:0.#},{this.Y:0.#} {this.Width:0.#}x{this.Height:0.#})";
}

/// <summary>HUD 布局配置（来自视觉清单的 hud 段，或用户配置覆盖）。</summary>
public sealed class HudLayoutConfig
{
    /// <summary>立绘最大宽度占视口宽度的比例。</summary>
    public float PortraitMaxWidthFraction { get; init; } = 0.18f;

    /// <summary>立绘最大高度占视口高度的比例。</summary>
    public float PortraitMaxHeightFraction { get; init; } = 0.40f;

    /// <summary>任务栏最大宽度占视口宽度的比例。</summary>
    public float TasksMaxWidthFraction { get; init; } = 0.25f;

    /// <summary>最多显示的任务条数。</summary>
    public int MaxVisibleTasks { get; init; } = 3;

    /// <summary>安全边距（UI 像素，再乘以用户缩放）。</summary>
    public float MarginUiPixels { get; init; } = 16f;

    /// <summary>用户缩放（1 为默认）。</summary>
    public float UserScale { get; init; } = 1f;

    /// <summary>立绘源图宽高比（宽/高）。</summary>
    public float PortraitAspect { get; init; } = 2f / 3f;
}

/// <summary>参与布局计算的环境量（全部为 UI 像素，已由游戏 UI 缩放换算过）。</summary>
public sealed class HudLayoutInput
{
    /// <summary>UI 视口宽度。</summary>
    public float ViewportWidth { get; init; }

    /// <summary>UI 视口高度。</summary>
    public float ViewportHeight { get; init; }

    /// <summary>原版底部工具栏占据的高度。</summary>
    public float ToolbarHeight { get; init; }

    /// <summary>右上角原版时钟与钱币区域占据的高度（任务栏必须避开）。</summary>
    public float TopRightReservedHeight { get; init; }

    /// <summary>立绘单行最多容纳的任务数下限（用于小窗口折叠）。</summary>
    public int MinPortraitPixelsForTasks { get; init; } = 0;
}

/// <summary>布局结果。</summary>
public sealed class HudLayoutResult
{
    /// <summary>立绘矩形；null 表示不显示。</summary>
    public LayoutRect? Portrait { get; init; }

    /// <summary>任务栏矩形；null 表示不显示。</summary>
    public LayoutRect? TaskPanel { get; init; }

    /// <summary>实际可显示的任务条数（0 表示折叠）。</summary>
    public int VisibleTaskCount { get; init; }

    /// <summary>是否允许显示立绘。</summary>
    public bool ShowPortrait => this.Portrait.HasValue && !this.Portrait.Value.IsEmpty;

    /// <summary>是否允许显示任务栏。</summary>
    public bool ShowTaskPanel => this.TaskPanel.HasValue && !this.TaskPanel.Value.IsEmpty && this.VisibleTaskCount > 0;
}

/// <summary>
/// HUD 布局计算（纯函数）。约定：
/// 1) 输入已是"实际 UI 坐标"，计算过程中只乘一次用户缩放，绝不再乘游戏 UI 缩放。
/// 2) 所有输出矩形必须落在视口内，且不进入底部工具栏区域、不进入右上角时钟/钱币区域。
/// 3) 立绘与任务栏互不重叠；空间不足时优先保留任务栏，折叠立绘。
/// </summary>
public static class HudLayoutCalculator
{
    /// <summary>计算布局。</summary>
    public static HudLayoutResult Compute(HudLayoutInput input, HudLayoutConfig config)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(config);

        float vw = input.ViewportWidth;
        float vh = input.ViewportHeight;
        if (vw <= 1f || vh <= 1f)
        {
            return new HudLayoutResult { Portrait = null, TaskPanel = null, VisibleTaskCount = 0 };
        }

        float scale = config.UserScale <= 0f ? 1f : config.UserScale;
        float margin = Math.Max(0f, config.MarginUiPixels * scale);

        // 可用高度：视口减去底部工具栏与上下边距。
        float toolbarTop = vh - Math.Max(0f, input.ToolbarHeight);
        float portraitAreaBottom = toolbarTop - margin;
        float portraitAreaTop = margin;

        // ---- 立绘：靠左下，但整体落在工具栏之上 ----
        float maxPortraitW = vw * Math.Clamp(config.PortraitMaxWidthFraction, 0.02f, 0.9f) * scale;
        float maxPortraitH = vh * Math.Clamp(config.PortraitMaxHeightFraction, 0.02f, 0.9f) * scale;

        float portraitH = maxPortraitH;
        float portraitW = portraitH * (config.PortraitAspect <= 0f ? 2f / 3f : config.PortraitAspect);
        if (portraitW > maxPortraitW)
        {
            portraitW = maxPortraitW;
            portraitH = portraitW / (config.PortraitAspect <= 0f ? 2f / 3f : config.PortraitAspect);
        }

        // 高度不能溢出可用竖直空间
        float availableH = portraitAreaBottom - portraitAreaTop;
        if (portraitH > availableH)
        {
            portraitH = Math.Max(0f, availableH);
            portraitW = portraitH * (config.PortraitAspect <= 0f ? 2f / 3f : config.PortraitAspect);
        }

        LayoutRect? portrait = null;
        if (portraitW >= 8f && portraitH >= 8f)
        {
            portrait = new LayoutRect(margin, portraitAreaBottom - portraitH, portraitW, portraitH);
        }

        // ---- 任务栏：右上，避开原版时钟与钱币 ----
        float tasksW = vw * Math.Clamp(config.TasksMaxWidthFraction, 0.05f, 0.9f) * scale;
        float tasksY = margin + Math.Max(0f, input.TopRightReservedHeight) * scale;
        float tasksAvailableH = portraitAreaBottom - tasksY;

        LayoutRect? taskPanel = null;
        int visibleTasks = Math.Clamp(config.MaxVisibleTasks, 0, 5);
        if (tasksW >= 40f && tasksAvailableH >= 20f)
        {
            float tasksH = Math.Min(tasksAvailableH, visibleTasks * 34f * scale);
            taskPanel = new LayoutRect(vw - margin - tasksW, tasksY, tasksW, tasksH);

            // 小窗口折叠：每个任务条最少需要 ~26 行高，放不下就减少条数。
            int fit = visibleTasks;
            float perTask = 34f * scale;
            while (fit > 0 && tasksH < fit * Math.Min(perTask, 26f * scale))
                fit--;
            visibleTasks = fit;
            if (visibleTasks <= 0)
                taskPanel = null;
        }
        else
        {
            visibleTasks = 0;
        }

        // ---- 互斥：立绘不能压住任务栏，也不能溢出视口 ----
        if (portrait is not null && taskPanel is not null && portrait.Value.IntersectsWith(taskPanel.Value))
        {
            portrait = null;
        }

        if (portrait is not null && !portrait.Value.IsInside(vw, vh))
            portrait = null;
        if (taskPanel is not null && !taskPanel.Value.IsInside(vw, vh))
            taskPanel = null;

        return new HudLayoutResult
        {
            Portrait = portrait,
            TaskPanel = taskPanel,
            VisibleTaskCount = taskPanel is null ? 0 : visibleTasks
        };
    }
}
