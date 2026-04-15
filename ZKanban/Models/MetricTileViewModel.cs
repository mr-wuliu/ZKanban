using Avalonia.Media;

namespace ZKanban.Models;

public sealed class MetricTileViewModel
{
    public string AccentDot { get; init; } = "●";

    public IBrush AccentBrush { get; init; } = Brushes.DodgerBlue;

    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}
