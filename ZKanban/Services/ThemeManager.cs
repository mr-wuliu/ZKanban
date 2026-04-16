using Avalonia;
using Avalonia.Media;
using ZKanban.Models;

namespace ZKanban.Services;

/// <summary>
/// Manages runtime theme switching.
/// Call <see cref="ApplyTheme"/> to set all <c>Theme*Brush</c> resources in
/// <see cref="Application.Resources"/>; AXAML files reference them via
/// <c>{DynamicResource Theme*Brush}</c>.
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, ThemeColors> Themes = new()
    {
        ["ocean"] = ThemeColors.Ocean,
        ["coffee"] = ThemeColors.Coffee,
    };

    public static string CurrentThemeName { get; private set; } = "ocean";

    public static ThemeColors Current => Themes.GetValueOrDefault(CurrentThemeName);

    public static IReadOnlyList<string> AvailableThemes => [.. Themes.Keys];

    public static void ApplyTheme(string themeName)
    {
        if (!Themes.ContainsKey(themeName))
            themeName = "ocean";

        CurrentThemeName = themeName;
        var t = Current;
        var app = Application.Current!;

        // Backgrounds
        SetBrush(app, "ThemeBgDeepBrush", t.BgDeep);
        SetBrush(app, "ThemeBgBaseBrush", t.BgBase);
        SetBrush(app, "ThemeBgSurfaceBrush", t.BgSurface);
        SetBrush(app, "ThemeBgElevatedBrush", t.BgElevated);
        SetBrush(app, "ThemeBgToolbarBrush", t.BgToolbar);

        // Borders
        SetBrush(app, "ThemeBorderDefaultBrush", t.BorderDefault);
        SetBrush(app, "ThemeBorderLightBrush", t.BorderLight);
        SetBrush(app, "ThemeBorderSubtleBrush", t.BorderSubtle);
        SetBrush(app, "ThemeBorderTileBrush", t.BorderTile);

        // Text
        SetBrush(app, "ThemeTextPrimaryBrush", t.TextPrimary);
        SetBrush(app, "ThemeTextContentBrush", t.TextContent);
        SetBrush(app, "ThemeTextSecondaryBrush", t.TextSecondary);
        SetBrush(app, "ThemeTextLabelBrush", t.TextLabel);
        SetBrush(app, "ThemeTextMutedBrush", t.TextMuted);
        SetBrush(app, "ThemeTextDimBrush", t.TextDim);

        // Interactive states
        SetBrush(app, "ThemeHoverBgBrush", t.HoverBg);
        SetBrush(app, "ThemePressedBgBrush", t.PressedBg);
        SetBrush(app, "ThemeCheckedBgBrush", t.CheckedBg);

        // Accent
        SetBrush(app, "ThemeAccentBrush", t.Accent);
        SetBrush(app, "ThemeAccentDeepBrush", t.AccentDeep);
        SetBrush(app, "ThemeAccentBrightBrush", t.AccentBright);
        SetBrush(app, "ThemeAccentPressedBrush", t.AccentPressed);

        // Chart
        SetBrush(app, "ThemeGridLineBrush", t.GridLine);
        SetBrush(app, "ThemeAxisLineBrush", t.AxisLine);
        SetBrush(app, "ThemeScrollbarBrush", t.Scrollbar);

        // Semi-transparent overlays
        SetBrush(app, "ThemeRootBgBrush", t.RootBg);
        SetBrush(app, "ThemeRootBorderBrush", t.RootBorder);
        SetBrush(app, "ThemeCardBgBrush", t.CardBg);
    }

    private static void SetBrush(Application app, string key, string hex)
    {
        app.Resources[key] = new SolidColorBrush(Color.Parse(hex));
    }
}
