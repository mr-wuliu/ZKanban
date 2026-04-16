namespace ZKanban.Models;

/// <summary>
/// Defines all color tokens for a single theme.
/// Each property maps to a <c>SolidColorBrush</c> resource keyed as <c>Theme{Name}Brush</c>.
/// </summary>
public sealed record ThemeColors
{
    // --- Backgrounds (darkest → lightest) ---
    public string BgDeep { get; init; } = "";
    public string BgBase { get; init; } = "";
    public string BgSurface { get; init; } = "";
    public string BgElevated { get; init; } = "";
    public string BgToolbar { get; init; } = "";

    // --- Borders ---
    public string BorderDefault { get; init; } = "";
    public string BorderLight { get; init; } = "";
    public string BorderSubtle { get; init; } = "";
    public string BorderTile { get; init; } = "";

    // --- Text (brightest → dimmest) ---
    public string TextPrimary { get; init; } = "";
    public string TextContent { get; init; } = "";
    public string TextSecondary { get; init; } = "";
    public string TextLabel { get; init; } = "";
    public string TextMuted { get; init; } = "";
    public string TextDim { get; init; } = "";

    // --- Interactive States ---
    public string HoverBg { get; init; } = "";
    public string PressedBg { get; init; } = "";
    public string CheckedBg { get; init; } = "";

    // --- Accent ---
    public string Accent { get; init; } = "";
    public string AccentDeep { get; init; } = "";
    public string AccentBright { get; init; } = "";
    public string AccentPressed { get; init; } = "";

    // --- Chart ---
    public string GridLine { get; init; } = "";
    public string AxisLine { get; init; } = "";
    public string Scrollbar { get; init; } = "";

    // --- Semi-transparent Overlays ---
    public string RootBg { get; init; } = "";
    public string RootBorder { get; init; } = "";
    public string CardBg { get; init; } = "";

    // --- Curve Palette ---
    public string[] Palette { get; init; } = [];

    // ────────────────────────────────────────
    //  Built-in themes
    // ────────────────────────────────────────

    /// <summary>Current default — cool navy/blue tones.</summary>
    public static ThemeColors Ocean { get; } = new()
    {
        BgDeep = "#0F1A2A",
        BgBase = "#132234",
        BgSurface = "#17212E",
        BgElevated = "#1A2744",
        BgToolbar = "#14304A",
        BorderDefault = "#27445C",
        BorderLight = "#33536D",
        BorderSubtle = "#22374D",
        BorderTile = "#263548",
        TextPrimary = "#F8FAFC",
        TextContent = "#E2E8F0",
        TextSecondary = "#94A3B8",
        TextLabel = "#CBD5E1",
        TextMuted = "#A8B8CC",
        TextDim = "#4A6A88",
        HoverBg = "#1C3650",
        PressedBg = "#214867",
        CheckedBg = "#152A40",
        Accent = "#3B82F6",
        AccentDeep = "#2563EB",
        AccentBright = "#67A6FF",
        AccentPressed = "#1D4ED8",
        GridLine = "#2A3E54",
        AxisLine = "#4A6A88",
        Scrollbar = "#3B5575",
        RootBg = "#CC0B1220",
        RootBorder = "#46DDE7F2",
        CardBg = "#A6111827",
        Palette = ["#67A6FF", "#FF9D4B", "#60E6B2", "#D38FFF", "#FFD166", "#7DD3FC"],
    };

    /// <summary>Warm coffee/espresso tones — inspired by daisyUI coffee theme.</summary>
    public static ThemeColors Coffee { get; } = new()
    {
        BgDeep = "#1A1412",
        BgBase = "#261E1A",
        BgSurface = "#332820",
        BgElevated = "#2D2218",
        BgToolbar = "#332820",
        BorderDefault = "#4D3B2E",
        BorderLight = "#5C4A3C",
        BorderSubtle = "#3D2E24",
        BorderTile = "#4D3F33",
        TextPrimary = "#F2E6D9",
        TextContent = "#DFD0BF",
        TextSecondary = "#A89480",
        TextLabel = "#C4B49E",
        TextMuted = "#8B7B6A",
        TextDim = "#6B5D4E",
        HoverBg = "#4A3628",
        PressedBg = "#5A4230",
        CheckedBg = "#3A2820",
        Accent = "#C49A6C",
        AccentDeep = "#A88050",
        AccentBright = "#D4AA7C",
        AccentPressed = "#987040",
        GridLine = "#3A2E24",
        AxisLine = "#5D4A3E",
        Scrollbar = "#5D4A3E",
        RootBg = "#CC1A1412",
        RootBorder = "#46F2E6D9",
        CardBg = "#A6332820",
        Palette = ["#C49A6C", "#E09070", "#90B880", "#C08080", "#E8C870", "#B0A090"],
    };
}
