namespace ZhipuUsageWidget.Models;

public sealed class ModelUsageSeries
{
    public string Label { get; init; } = string.Empty;

    public string DisplayValue { get; init; } = string.Empty;

    public double TotalValue { get; init; }

    public IReadOnlyList<UsageSeriesPoint> Points { get; init; } = [];
}
