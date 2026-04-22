using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Platform;
using ZKanban.Models;

namespace ZKanban.Services;

public sealed class BigModelAutomationService
{
    public const string UsageUrl = "https://bigmodel.cn/coding-plan/personal/usage";
    public const string LoginUrl = "https://bigmodel.cn/login?redirect=%2Fcoding-plan%2Fpersonal%2Fusage";

    private sealed record AuthContext(string Token, string Organization, string Project);

    private AuthContext? _cachedAuth;

    private static readonly string WebViewUserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZKanban",
        "WebView2");

    /// <summary>
    /// Configures the NativeWebView environment (user data folder, disable dev tools).
    /// Wire this up to the NativeWebView.EnvironmentRequested event in XAML or code-behind.
    /// </summary>
    internal static void ConfigureWebViewEnvironment(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;
        if (e is WindowsWebView2EnvironmentRequestedEventArgs webView2)
        {
            webView2.UserDataFolder = WebViewUserDataFolder;
        }
    }

    public async Task<UsageSnapshot> RefreshUsageAsync(NativeWebView webView, CredentialSettings settings, DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken)
    {
        var loginState = await GetLoginStateAsync(webView, settings, settings.AutoLogin, cancellationToken);
        if (!loginState.IsLoggedIn)
        {
            return new UsageSnapshot
            {
                Status = "等待登录完成",
                LastUpdated = DateTimeOffset.Now,
                RawSummary = loginState.Message,
                IsLoggedIn = false,
                CurrentUrl = loginState.CurrentUrl,
                RangeDays = settings.RangeDays,
            };
        }

        var quotaResult = await FetchUsageDataAsync<QuotaApiResponse>(webView, "/api/monitor/usage/quota/limit");
        var (Start, End, DisplayText) = BuildRange(settings.RangeDays, startDate, endDate);
        var query = $"/api/monitor/usage/model-usage?startTime={Uri.EscapeDataString(Start)}&endTime={Uri.EscapeDataString(End)}";
        var usageResult = await FetchUsageDataAsync<ModelUsageApiResponse>(webView, query);

        var quotas = ExtractQuotas(quotaResult);
        var series = ExtractSeries(usageResult);
        var totalSeries = ExtractTotalSeries(usageResult);
        WidgetTrace.Write($"Usage API parsed. Quotas={quotas.Count}; Series={series.Count}; FirstSeries={(series.FirstOrDefault()?.Label ?? "<none>")}");

        return new UsageSnapshot
        {
            Status = "同步成功",
            LastUpdated = DateTimeOffset.Now,
            Quotas = quotas,
            ModelUsages = series,
            TotalUsageSeries = totalSeries,
            RawSummary = $"当前页面: {loginState.CurrentUrl}\n范围: {DisplayText}",
            IsLoggedIn = true,
            CurrentUrl = loginState.CurrentUrl,
            RangeDays = settings.RangeDays,
        };
    }

    public Task<UsageSnapshot> RefreshUsageAsync(NativeWebView webView, CredentialSettings settings, CancellationToken cancellationToken)
    {
        return RefreshUsageAsync(webView, settings, null, null, cancellationToken);
    }

    /// <summary>
    /// Fetches daily-granularity usage records for a date range (for backfilling history cache).
    /// Each returned tuple contains the date and a list of per-model token totals for that day.
    /// </summary>
    public async Task<List<(DateOnly Date, List<ModelDailyUsage> Models)>> FetchDailyUsageRecordsAsync(
        NativeWebView webView, CredentialSettings settings, DateOnly start, DateOnly end, CancellationToken ct)
    {
        // Skip full login check if auth is already cached (caller already verified login in Step 1)
        if (_cachedAuth is null)
        {
            var loginState = await GetLoginStateAsync(webView, settings, settings.AutoLogin, ct);
            if (!loginState.IsLoggedIn)
            {
                return [];
            }
        }

        var startTime = start.ToDateTime(TimeOnly.MinValue);
        var endTime = end.ToDateTime(new TimeOnly(23, 59, 59));
        var query = $"/api/monitor/usage/model-usage?startTime={Uri.EscapeDataString(startTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}&endTime={Uri.EscapeDataString(endTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}";
        var usageResult = await FetchUsageDataAsync<ModelUsageApiResponse>(webView, query);

        if (usageResult?.Data?.XTime is null || usageResult.Data.ModelDataList is null)
        {
            return [];
        }

        // The API may return hourly or daily granularity depending on the range.
        // Group by date and sum token values so we always get daily totals.
        var dailySums = new Dictionary<DateOnly, Dictionary<string, double>>();
        var modelOrder = new List<string>();

        for (var i = 0; i < usageResult.Data.XTime.Count; i++)
        {
            if (!DateTime.TryParse(usageResult.Data.XTime[i], out var time))
            {
                continue;
            }

            var date = DateOnly.FromDateTime(time);

            if (!dailySums.TryGetValue(date, out var dateModels))
            {
                dateModels = new Dictionary<string, double>();
                dailySums[date] = dateModels;
            }

            foreach (var item in usageResult.Data.ModelDataList)
            {
                if (string.IsNullOrWhiteSpace(item.ModelName))
                {
                    continue;
                }

                var tokens = item.TokensUsage is not null && i < item.TokensUsage.Count
                    ? item.TokensUsage[i]
                    : 0d;

                var name = item.ModelName.Trim();
                if (dateModels.TryAdd(name, tokens))
                {
                    if (!modelOrder.Contains(name))
                    {
                        modelOrder.Add(name);
                    }
                }
                else
                {
                    dateModels[name] += tokens;
                }
            }
        }

        var records = new List<(DateOnly Date, List<ModelDailyUsage> Models)>();
        foreach (var (date, models) in dailySums.OrderBy(kv => kv.Key))
        {
            records.Add((date, modelOrder
                .Where(name => models.ContainsKey(name))
                .Select(name => new ModelDailyUsage
                {
                    Name = name,
                    Tokens = models[name],
                }).ToList()));
        }

        return records;
    }

    public static async Task<LoginStateInfo> GetLoginStateAsync(NativeWebView webView, CredentialSettings settings, bool allowAutoLogin, CancellationToken cancellationToken)
    {
        await NavigateAsync(webView, UsageUrl, cancellationToken);
        await WaitForDocumentReadyAsync(webView, cancellationToken);

        var currentUrl = await GetLocationAsync(webView);
        if (allowAutoLogin && NeedsLogin(currentUrl) && !string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password))
        {
            await TryAutoLoginAsync(webView, settings, cancellationToken);
            currentUrl = await GetLocationAsync(webView);
        }

        var bodyText = await GetBodyTextAsync(webView);
        var isLoggedIn = !NeedsLogin(currentUrl) && !bodyText.Contains("完成登录/注册", StringComparison.OrdinalIgnoreCase);

        return new LoginStateInfo
        {
            IsLoggedIn = isLoggedIn,
            CurrentUrl = currentUrl,
            Message = isLoggedIn ? "已登录，可直接同步图表。" : "当前仍在登录页。",
        };
    }

    public async Task<List<string>> FetchAvailableModelsAsync(NativeWebView webView, CredentialSettings settings, CancellationToken cancellationToken)
    {
        var loginState = await GetLoginStateAsync(webView, settings, settings.AutoLogin, cancellationToken);
        if (!loginState.IsLoggedIn)
        {
            return [];
        }

        var (Start, End, DisplayText) = BuildRange(7, null, null);
        var query = $"/api/monitor/usage/model-usage?startTime={Uri.EscapeDataString(Start)}&endTime={Uri.EscapeDataString(End)}";
        var usageResult = await FetchUsageDataAsync<ModelUsageApiResponse>(webView, query);

        return usageResult?.Data?.ModelSummaryList?
            .Where(item => !string.IsNullOrWhiteSpace(item.ModelName))
            .Select(item => item.ModelName.Trim())
            .ToList() ?? [];
    }

    public static async Task OpenLoginPageAsync(NativeWebView webView, CancellationToken cancellationToken)
    {
        await NavigateAsync(webView, LoginUrl, cancellationToken);
        await WaitForDocumentReadyAsync(webView, cancellationToken);
    }

    public async Task LogoutAsync(NativeWebView webView, CancellationToken cancellationToken)
    {
        _cachedAuth = null;
        var cookieManager = webView.TryGetCookieManager();
        if (cookieManager is not null)
        {
            var cookies = await cookieManager.GetCookiesAsync();
            foreach (var cookie in cookies)
            {
                cookieManager.DeleteCookie(cookie.Name, cookie.Domain, cookie.Path);
            }
        }
        await NavigateAsync(webView, LoginUrl, cancellationToken);
    }

    private static async Task NavigateAsync(NativeWebView webView, string url, CancellationToken cancellationToken)
    {
        var current = webView.Source?.ToString() ?? string.Empty;
        if (current.TrimEnd('/').Equals(url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, WebViewNavigationCompletedEventArgs args)
        {
            webView.NavigationCompleted -= Handler;
            if (args.IsSuccess)
            {
                tcs.TrySetResult();
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException("页面加载失败。"));
            }
        }

        webView.NavigationCompleted += Handler;
        webView.Source = new Uri(url);
        using var registration = cancellationToken.Register(() =>
        {
            webView.NavigationCompleted -= Handler;
            tcs.TrySetCanceled(cancellationToken);
        });

        await tcs.Task;
    }

    private static async Task WaitForDocumentReadyAsync(NativeWebView webView, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 30; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readyState = await ExecuteJsonAsync<string>(webView, "document.readyState");
            if (string.Equals(readyState, "complete", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            await Task.Delay(500, cancellationToken);
        }
    }

    private static bool NeedsLogin(string currentUrl)
    {
        return currentUrl.Contains("login", StringComparison.OrdinalIgnoreCase)
               || currentUrl.Contains("passport", StringComparison.OrdinalIgnoreCase)
               || currentUrl.Contains("auth", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task TryAutoLoginAsync(NativeWebView webView, CredentialSettings settings, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            const string switchTabScript = """
                (() => {
                    const visible = (el) => {
                      if (!el) return false;
                      const style = window.getComputedStyle(el);
                      const rect = el.getBoundingClientRect();
                      return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                    };
                    const tab = [...document.querySelectorAll('.el-tabs__item')]
                      .find(el => visible(el) && /账号登录/i.test((el.innerText || el.textContent || '').trim()));
                    if (tab) tab.click();
                    return !!tab;
                })();
                """;

            var loginScript = $$"""
                (() => {
                    const visible = (el) => {
                      if (!el) return false;
                      const style = window.getComputedStyle(el);
                      const rect = el.getBoundingClientRect();
                      return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                    };
                    const setValue = (selector, value) => {
                      const input = [...document.querySelectorAll(selector)].find(visible);
                      if (!input) return false;
                      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
                      if (setter) setter.call(input, value); else input.value = value;
                      input.dispatchEvent(new Event('input', { bubbles: true }));
                      input.dispatchEvent(new Event('change', { bubbles: true }));
                      return true;
                    };
                    setValue('input[placeholder*="用户名"], input[placeholder*="邮箱"], input[placeholder*="手机号"]', {{JsonSerializer.Serialize(settings.Username)}});
                    setValue('input[type="password"], input[placeholder*="密码"]', {{JsonSerializer.Serialize(settings.Password)}});
                    const button = [...document.querySelectorAll('button')].find(el => visible(el) && /^登录$/.test((el.innerText || '').trim()));
                    if (button) { button.click(); return true; }
                    return false;
                })();
                """;

            await ExecuteJsonAsync<bool>(webView, switchTabScript);
            await Task.Delay(400, cancellationToken);
            await ExecuteJsonAsync<bool>(webView, loginScript);
            await Task.Delay(2500, cancellationToken);
            var currentUrl = await GetLocationAsync(webView);
            if (!NeedsLogin(currentUrl))
            {
                await WaitForDocumentReadyAsync(webView, cancellationToken);
                return;
            }
        }
    }

    private async Task<T?> FetchUsageDataAsync<T>(NativeWebView webView, string relativeUrl)
    {
        if (_cachedAuth is null)
        {
            var cookieManager = webView.TryGetCookieManager()
                ?? throw new InvalidOperationException("WebView 未初始化或 Cookie 管理器不可用。");
            var cookies = await cookieManager.GetCookiesAsync();
            var token = cookies.FirstOrDefault(cookie => cookie.Name.Equals("bigmodel_token_production", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
            var organization = await ExecuteJsonAsync<string>(webView, "localStorage.getItem('Bigmodel-Organization') || ''");
            var project = await ExecuteJsonAsync<string>(webView, "localStorage.getItem('Bigmodel-Project') || ''");
            WidgetTrace.Write($"Fetch headers tokenLen={token.Length}, org={organization}, project={project}");
            _cachedAuth = new AuthContext(token, organization, project);
        }

        var script = $$"""
            (() => {
              const xhr = new XMLHttpRequest();
              xhr.open('GET', {{JsonSerializer.Serialize(relativeUrl)}}, false);
              xhr.withCredentials = true;
              xhr.setRequestHeader('Authorization', {{JsonSerializer.Serialize(Uri.UnescapeDataString(_cachedAuth.Token))}});
              xhr.setRequestHeader('Bigmodel-Organization', {{JsonSerializer.Serialize(_cachedAuth.Organization)}});
              xhr.setRequestHeader('Bigmodel-Project', {{JsonSerializer.Serialize(_cachedAuth.Project)}});
              xhr.setRequestHeader('Set-Language', 'zh');
              xhr.send();
              return xhr.responseText;
            })();
            """;
        var raw = await ExecuteJsonAsync<string>(webView, script);
        WidgetTrace.Write($"Fetch {relativeUrl} => {raw[..Math.Min(raw.Length, 240)]}");
        return JsonSerializer.Deserialize<T>(raw);
    }

    private static async Task<string> GetLocationAsync(NativeWebView webView)
    {
        return await ExecuteJsonAsync<string>(webView, "window.location.href");
    }

    private static async Task<string> GetBodyTextAsync(NativeWebView webView)
    {
        return await ExecuteJsonAsync<string>(webView, "(() => document.body ? document.body.innerText : '')()");
    }

    private static async Task<T> ExecuteJsonAsync<T>(NativeWebView webView, string script)
    {
        var raw = await webView.InvokeScript(script) ?? throw new InvalidOperationException("脚本返回空结果。");
        if (typeof(T) == typeof(string))
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return (T)(object)(doc.RootElement.GetString() ?? string.Empty);
            }

            return (T)(object)doc.RootElement.GetRawText();
        }

        var value = JsonSerializer.Deserialize<T>(raw) ?? throw new InvalidOperationException("脚本返回空结果。");
        return value;
    }

    private static (string Start, string End, string DisplayText) BuildRange(int rangeDays, DateTime? startDate, DateTime? endDate)
    {
        if (startDate.HasValue && endDate.HasValue)
        {
            var customStart = startDate.Value.Date;
            var customEnd = endDate.Value.Date.AddDays(1).AddSeconds(-1);
            return (
                customStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                customEnd.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                $"{customStart:yyyy-MM-dd} ~ {customEnd:yyyy-MM-dd}");
        }

        var now = DateTime.Now;
        var defaultStart = rangeDays switch
        {
            1 => now.Date,
            30 => now.Date.AddDays(-29),
            _ => now.Date.AddDays(-6),
        };
        var defaultEnd = now.Date.AddDays(1).AddSeconds(-1);
        return (
            defaultStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            defaultEnd.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            $"{defaultStart:yyyy-MM-dd} ~ {defaultEnd:yyyy-MM-dd}");
    }

    private static List<UsageQuotaMetric> ExtractQuotas(QuotaApiResponse? response)
    {
        return response?.Data?.Limits?.Select(item => new UsageQuotaMetric
        {
            Label = item.Type == "TIME_LIMIT" ? "MCP 每月额度" : "每5小时使用额度",
            Percent = item.Percentage,
            PercentText = $"{item.Percentage}%",
            ResetText = item.NextResetTime > 0
                ? $"重置时间：{DateTimeOffset.FromUnixTimeMilliseconds(item.NextResetTime).LocalDateTime:MM-dd HH:mm}"
                : string.Empty,
        }).ToList() ?? [];
    }

    private static List<ModelUsageSeries> ExtractSeries(ModelUsageApiResponse? response)
    {
        if (response?.Data?.ModelDataList is null || response.Data.XTime is null)
        {
            return [];
        }

        var series = new List<ModelUsageSeries>();
        foreach (var item in response.Data.ModelDataList)
        {
            if (string.IsNullOrWhiteSpace(item.ModelName))
            {
                continue;
            }

            var label = item.ModelName;
            var points = new List<UsageSeriesPoint>();
            for (var i = 0; i < Math.Min(response.Data.XTime.Count, item.TokensUsage?.Count ?? 0); i++)
            {
                if (!DateTime.TryParse(response.Data.XTime[i], out var time))
                {
                    continue;
                }

                points.Add(new UsageSeriesPoint
                {
                    Time = time,
                    Value = item.TokensUsage![i],
                });
            }

            series.Add(new ModelUsageSeries
            {
                Label = label,
                DisplayValue = FormatCompactNumber(item.TotalTokens),
                TotalValue = item.TotalTokens,
                Points = points,
            });
        }

        return [.. series.OrderByDescending(item => item.TotalValue)];
    }

    private static ModelUsageSeries? ExtractTotalSeries(ModelUsageApiResponse? response)
    {
        if (response?.Data?.XTime is null || response.Data.ModelDataList is null)
        {
            return null;
        }

        var points = new List<UsageSeriesPoint>();
        for (var i = 0; i < response.Data.XTime.Count; i++)
        {
            if (!DateTime.TryParse(response.Data.XTime[i], out var time))
            {
                continue;
            }

            var total = response.Data.ModelDataList.Sum(item =>
                item.TokensUsage is not null && i < item.TokensUsage.Count ? item.TokensUsage[i] : 0d);

            points.Add(new UsageSeriesPoint
            {
                Time = time,
                Value = total,
            });
        }

        return new ModelUsageSeries
        {
            Label = "总用量",
            DisplayValue = FormatCompactNumber(points.Sum(p => p.Value)),
            TotalValue = points.Sum(p => p.Value),
            Points = points,
        };
    }

    private static string FormatCompactNumber(double value)
    {
        if (value >= 1_000_000)
        {
            return $"{value / 1_000_000d:0.##} M";
        }

        if (value >= 1_000)
        {
            return $"{value / 1_000d:0.##} K";
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private sealed class QuotaApiResponse
    {
        [JsonPropertyName("data")]
        public QuotaApiData? Data { get; set; }
    }

    private sealed class QuotaApiData
    {
        [JsonPropertyName("limits")]
        public List<QuotaApiItem>? Limits { get; set; }
    }

    private sealed class QuotaApiItem
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        [JsonPropertyName("percentage")]
        public double Percentage { get; set; }
        [JsonPropertyName("nextResetTime")]
        public long NextResetTime { get; set; }
    }

    private sealed class ModelUsageApiResponse
    {
        [JsonPropertyName("data")]
        public ModelUsageApiData? Data { get; set; }
    }

    private sealed class ModelUsageApiData
    {
        [JsonPropertyName("x_time")]
        public List<string>? XTime { get; set; }

        [JsonPropertyName("modelDataList")]
        public List<ModelDataItem>? ModelDataList { get; set; }

        [JsonPropertyName("modelSummaryList")]
        public List<ModelSummaryItem>? ModelSummaryList { get; set; }
    }

    private sealed class ModelDataItem
    {
        [JsonPropertyName("modelName")]
        public string ModelName { get; set; } = string.Empty;

        [JsonPropertyName("tokensUsage")]
        public List<double>? TokensUsage { get; set; }

        [JsonPropertyName("totalTokens")]
        public double TotalTokens { get; set; }
    }

    private sealed class ModelSummaryItem
    {
        [JsonPropertyName("modelName")]
        public string ModelName { get; set; } = string.Empty;

        [JsonPropertyName("totalTokens")]
        public double TotalTokens { get; set; }
    }
}
