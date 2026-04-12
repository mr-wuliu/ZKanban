namespace ZhipuUsageWidget.Models;

public sealed class UsageSnapshot
{
    public string Status { get; init; } = "等待同步";

    public DateTimeOffset? LastUpdated { get; init; }

    public IReadOnlyList<UsageQuotaMetric> Quotas { get; init; } = [];

    public IReadOnlyList<ModelUsageSeries> ModelUsages { get; init; } = [];

    public ModelUsageSeries? TotalUsageSeries { get; init; }

    public string RawSummary { get; init; } = string.Empty;

    public bool IsLoggedIn { get; init; }

    public string CurrentUrl { get; init; } = string.Empty;

    public int RangeDays { get; init; } = 7;
}
