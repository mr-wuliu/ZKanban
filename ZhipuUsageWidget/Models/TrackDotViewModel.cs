using Avalonia.Media;

namespace ZhipuUsageWidget.Models;

public sealed class TrackDotViewModel
{
    public double Left { get; init; }
    public double Top { get; init; }
    public IBrush Stroke { get; init; } = Brushes.White;
}

public sealed class TrackSeriesItemViewModel
{
    public IBrush Stroke { get; init; } = Brushes.White;
    public string Text { get; init; } = string.Empty;
}
