using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZKanban.Services;

/// <summary>
/// Persists daily model-usage totals to disk (one JSON file per day).
/// Historical days are write-once; today is overwritten on each refresh.
/// Retention window is configurable (default 60 days).
/// </summary>
public sealed class UsageHistoryService
{
    private static readonly string HistoryFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZKanban",
        "history");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Dictionary<DateOnly, DailyUsageRecord> _records = [];

    public int RetentionDays { get; init; } = 60;

    /// <summary>
    /// Reads all cached daily records from disk. Call once at startup.
    /// </summary>
    public void LoadFromDisk()
    {
        Directory.CreateDirectory(HistoryFolder);
        foreach (var file in Directory.GetFiles(HistoryFolder, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!DateOnly.TryParseExact(fileName, "yyyy-MM-dd", out var date))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<DailyUsageRecord>(json);
                if (record is not null)
                {
                    _records[date] = record;
                }
            }
            catch
            {
                // Skip corrupt files
            }
        }

        Trim();
    }

    /// <summary>
    /// Writes all in-memory records to disk and deletes stale files beyond retention window.
    /// </summary>
    public void SaveToDisk()
    {
        Directory.CreateDirectory(HistoryFolder);

        foreach (var (date, record) in _records)
        {
            var path = GetFilePath(date);
            var json = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(path, json);
        }

        // Remove stale disk files
        var cutoff = DateOnly.FromDateTime(DateTime.Today).AddDays(-RetentionDays);
        foreach (var file in Directory.GetFiles(HistoryFolder, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (DateOnly.TryParseExact(fileName, "yyyy-MM-dd", out var date) && date < cutoff)
            {
                try { File.Delete(file); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Updates the cache for a single day (overwrites any existing entry).
    /// Callers should persist to disk via <see cref="SaveToDisk"/> after updates.
    /// </summary>
    public void UpdateDay(DateOnly date, IReadOnlyList<ModelDailyUsage> models)
    {
        _records[date] = new DailyUsageRecord
        {
            Date = date.ToString("yyyy-MM-dd"),
            Models = [.. models],
        };
    }

    /// <summary>
    /// Returns cached records for every day in [start, end] that has data.
    /// </summary>
    public IReadOnlyList<DailyUsageRecord> GetRange(DateOnly start, DateOnly end)
    {
        var result = new List<DailyUsageRecord>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (_records.TryGetValue(d, out var record))
            {
                result.Add(record);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the set of dates that are currently cached.
    /// </summary>
    public IReadOnlySet<DateOnly> CachedDates => _records.Keys.ToHashSet();

    /// <summary>
    /// Returns true if a record exists for the given date.
    /// </summary>
    public bool HasDate(DateOnly date) => _records.ContainsKey(date);

    /// <summary>
    /// Finds gaps in the cache within [start, end] and returns contiguous gap ranges.
    /// Each tuple is (rangeStart, rangeEnd) for a contiguous missing block.
    /// </summary>
    public IReadOnlyList<(DateOnly Start, DateOnly End)> FindGaps(DateOnly start, DateOnly end)
    {
        var gaps = new List<(DateOnly Start, DateOnly End)>();
        DateOnly? gapStart = null;

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (!_records.ContainsKey(d))
            {
                gapStart ??= d;
            }
            else if (gapStart is not null)
            {
                gaps.Add((gapStart.Value, d.AddDays(-1)));
                gapStart = null;
            }
        }

        if (gapStart is not null)
        {
            gaps.Add((gapStart.Value, end));
        }

        return gaps;
    }

    /// <summary>
    /// Removes records older than the retention window.
    /// </summary>
    public void Trim()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.Today).AddDays(-RetentionDays);
        var stale = _records.Keys.Where(d => d < cutoff).ToList();
        foreach (var d in stale)
        {
            _records.Remove(d);
        }
    }

    /// <summary>
    /// Returns the number of cached days.
    /// </summary>
    public int Count => _records.Count;

    /// <summary>
    /// Marks a day's data as verified (freshly confirmed against the API).
    /// </summary>
    public void MarkVerified(DateOnly date)
    {
        if (_records.TryGetValue(date, out var record))
        {
            record.LastVerified = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Returns dates within [start, end] that should be refreshed from the API.
    /// <list type="bullet">
    ///   <item>Days older than 3 days that have been verified are "final" — skipped.</item>
    ///   <item>Recent days (≤ 3 days ago) are skipped only if verified within the last 6 hours.</item>
    ///   <item>Missing days are always included.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<DateOnly> FindDaysNeedingRefresh(DateOnly start, DateOnly end)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = DateTimeOffset.Now;
        var result = new List<DateOnly>();

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var daysAgo = today.DayNumber - d.DayNumber;

            if (daysAgo > 3)
            {
                // Old days: skip if already verified at least once
                if (_records.TryGetValue(d, out var oldRecord) && !string.IsNullOrEmpty(oldRecord.LastVerified))
                {
                    continue;
                }
            }
            else
            {
                // Recent days: skip if verified within the last 6 hours
                if (_records.TryGetValue(d, out var recentRecord) &&
                    !string.IsNullOrEmpty(recentRecord.LastVerified) &&
                    DateTimeOffset.TryParse(recentRecord.LastVerified, out var verifiedAt) &&
                    now - verifiedAt < TimeSpan.FromHours(6))
                {
                    continue;
                }
            }

            result.Add(d);
        }

        return result;
    }

    private static string GetFilePath(DateOnly date) =>
        Path.Combine(HistoryFolder, $"{date:yyyy-MM-dd}.json");
}

/// <summary>
/// Single day's per-model usage totals. Serialized to disk as one JSON file.
/// </summary>
public sealed class DailyUsageRecord
{
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    [JsonPropertyName("models")]
    public List<ModelDailyUsage> Models { get; init; } = [];

    /// <summary>
    /// ISO 8601 timestamp of when this record was last verified against the API.
    /// Null means never verified (treated as stale).
    /// </summary>
    [JsonPropertyName("lastVerified")]
    public string? LastVerified { get; set; }
}

/// <summary>
/// One model's daily token total.
/// </summary>
public sealed class ModelDailyUsage
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("tokens")]
    public double Tokens { get; init; }
}
