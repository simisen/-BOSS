namespace LeliaFarm.Core.Configuration;

using StardewModdingAPI;

/// <summary>
/// 用户配置。写入 Mods/LeliaFarm.Core/config.json，由 SMAPI 管理读写。
/// 所有开关都必须能在不重启游戏的情况下按收益边界生效（HUD/雾立即生效）。
/// </summary>
public sealed class ModConfig
{
    /// <summary>是否显示本 Mod 的专属 HUD（占位立绘 + 任务栏）。</summary>
    public bool EnableHud { get; set; } = true;

    /// <summary>HUD 缩放（0.5 – 2.0）。</summary>
    public float HudScale { get; set; } = 1f;

    /// <summary>隐藏 HUD 的按键。</summary>
    public SButton ToggleHudKey { get; set; } = SButton.F9;

    /// <summary>是否显示占位立绘（关闭后只显示任务栏，不影响玩法）。</summary>
    public bool ShowPlaceholderPortrait { get; set; } = true;

    /// <summary>是否启用主题农场的雾效。</summary>
    public bool EnableFog { get; set; } = true;

    /// <summary>是否启用三项引导任务。</summary>
    public bool EnableIntroTasks { get; set; } = true;

    /// <summary>
    /// 开发命令开关，默认关闭。只有明确打开后才允许 <c>lelia_dev_setup</c> 在专用测试存档生成样片。
    /// </summary>
    public bool EnableDevCommands { get; set; } = false;

    /// <summary>把越界或无意义的配置夹回安全范围，并返回被修正的说明。</summary>
    public IReadOnlyList<string> Normalize()
    {
        var notes = new List<string>();

        if (this.HudScale < 0.5f || this.HudScale > 2f)
        {
            notes.Add($"HudScale={this.HudScale} 超出 [0.5,2]，已夹为 1.0。");
            this.HudScale = 1f;
        }

        return notes;
    }
}
