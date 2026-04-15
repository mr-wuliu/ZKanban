namespace ZKanban.Models;

public sealed class UsageQuotaMetric
{
    public string Label { get; init; } = string.Empty;

    public double Percent { get; init; }

    public string PercentText { get; init; } = string.Empty;

    public string ResetText { get; init; } = string.Empty;
}
