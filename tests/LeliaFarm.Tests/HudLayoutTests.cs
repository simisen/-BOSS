namespace LeliaFarm.Tests;

using LeliaFarm.Core.Domain.Hud;
using Xunit;

public class HudLayoutTests
{
    private static HudLayoutInput Input(float w, float h) => new()
    {
        ViewportWidth = w,
        ViewportHeight = h,
        ToolbarHeight = 96f,
        TopRightReservedHeight = 72f
    };

    private static HudLayoutConfig Config(float scale = 1f) => new()
    {
        UserScale = scale,
        MarginUiPixels = 16f,
        PortraitMaxWidthFraction = 0.18f,
        PortraitMaxHeightFraction = 0.40f,
        TasksMaxWidthFraction = 0.25f,
        MaxVisibleTasks = 3,
        PortraitAspect = 2f / 3f
    };

    [Theory]
    [InlineData(1280f, 720f)]
    [InlineData(1920f, 1080f)]
    [InlineData(2560f, 1440f)]
    public void LayoutStaysInsideViewportAndAvoidsReservedAreas(float w, float h)
    {
        var result = HudLayoutCalculator.Compute(Input(w, h), Config());

        Assert.NotNull(result.Portrait);
        Assert.NotNull(result.TaskPanel);

        var portrait = result.Portrait!.Value;
        var tasks = result.TaskPanel!.Value;

        Assert.True(portrait.IsInside(w, h), $"立绘溢出视口：{portrait}");
        Assert.True(tasks.IsInside(w, h), $"任务栏溢出视口：{tasks}");

        // 立绘整体在底部工具栏上方
        float toolbarTop = h - 96f;
        Assert.True(portrait.Bottom <= toolbarTop, $"立绘压住工具栏：{portrait.Bottom} > {toolbarTop}");

        // 任务栏在右上角保留区之下
        Assert.True(tasks.Y >= 72f, $"任务栏压住时钟/钱币：{tasks.Y} < 72");

        // 两者互不重叠
        Assert.False(portrait.IntersectsWith(tasks), "立绘与任务栏重叠");
    }

    [Fact]
    public void LayoutIsStableAcrossUiScale()
    {
        // 说明：输入已是 UI 坐标，UserScale 只乘一次；缩放后仍必须在视口内。
        var normal = HudLayoutCalculator.Compute(Input(1920f, 1080f), Config(1f));
        var large = HudLayoutCalculator.Compute(Input(1920f, 1080f), Config(2f));

        Assert.True(large.Portrait!.Value.Width > normal.Portrait!.Value.Width);
        Assert.True(large.Portrait.Value.IsInside(1920f, 1080f));
        Assert.True(large.TaskPanel!.Value.IsInside(1920f, 1080f));
    }

    [Fact]
    public void TinyViewport_CollapsesGracefully()
    {
        var result = HudLayoutCalculator.Compute(Input(160f, 120f), Config());

        Assert.False(result.ShowTaskPanel);
        Assert.Equal(0, result.VisibleTaskCount);
    }

    [Fact]
    public void ZeroViewport_ProducesNothing()
    {
        var result = HudLayoutCalculator.Compute(Input(0f, 0f), Config());

        Assert.False(result.ShowPortrait);
        Assert.False(result.ShowTaskPanel);
    }

    [Fact]
    public void TaskPanelNeverOverlapsToolbar()
    {
        var result = HudLayoutCalculator.Compute(Input(1280f, 720f), Config());

        var tasks = result.TaskPanel!.Value;
        Assert.True(tasks.Bottom <= 720f - 96f, $"任务栏进入工具栏区域：{tasks.Bottom}");
    }
}
