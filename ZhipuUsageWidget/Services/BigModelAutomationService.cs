using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ZhipuUsageWidget.Models;

namespace ZhipuUsageWidget.Services;

public sealed class BigModelAutomationService
{
    public const string UsageUrl = "https://bigmodel.cn/coding-plan/personal/usage";
    public const string LoginUrl = "https://bigmodel.cn/login?redirect=%2Fcoding-plan%2Fpersonal%2Fusage";

    private static readonly string WebViewUserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZhipuUsageWidget",
        "WebView2");

    public async Task<UsageSnapshot> RefreshUsageAsync(WebView2 webView, CredentialSettings settings, DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(webView);

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
        var range = BuildRange(settings.RangeDays, startDate, endDate);
        var query = $"/api/monitor/usage/model-usage?startTime={Uri.EscapeDataString(range.Start)}&endTime={Uri.EscapeDataString(range.End)}";
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
            RawSummary = $"当前页面: {loginState.CurrentUrl}\n范围: {range.DisplayText}",
            IsLoggedIn = true,
            CurrentUrl = loginState.CurrentUrl,
            RangeDays = settings.RangeDays,
        };
    }

    public Task<UsageSnapshot> RefreshUsageAsync(WebView2 webView, CredentialSettings settings, CancellationToken cancellationToken)
    {
        return RefreshUsageAsync(webView, settings, null, null, cancellationToken);
    }

    public async Task<LoginStateInfo> GetLoginStateAsync(WebView2 webView, CredentialSettings settings, bool allowAutoLogin, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(webView);
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

    public async Task<List<string>> FetchAvailableModelsAsync(WebView2 webView, CredentialSettings settings, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(webView);
        var loginState = await GetLoginStateAsync(webView, settings, settings.AutoLogin, cancellationToken);
        if (!loginState.IsLoggedIn)
        {
            return [];
        }

        var range = BuildRange(7, null, null);
        var query = $"/api/monitor/usage/model-usage?startTime={Uri.EscapeDataString(range.Start)}&endTime={Uri.EscapeDataString(range.End)}";
        var usageResult = await FetchUsageDataAsync<ModelUsageApiResponse>(webView, query);

        return usageResult?.Data?.ModelSummaryList?
            .Where(item => !string.IsNullOrWhiteSpace(item.ModelName))
            .Select(item => $"{item.ModelName.Trim()} 消耗")
            .ToList() ?? [];
    }

    public async Task OpenLoginPageAsync(WebView2 webView, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(webView);
        await NavigateAsync(webView, LoginUrl, cancellationToken);
        await WaitForDocumentReadyAsync(webView, cancellationToken);
    }

    public async Task LogoutAsync(WebView2 webView, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(webView);
        var core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 未初始化。");
        core.CookieManager.DeleteAllCookies();
        await core.Profile.ClearBrowsingDataAsync();
        await NavigateAsync(webView, LoginUrl, cancellationToken);
    }

    private static async Task EnsureInitializedAsync(WebView2 webView)
    {
        if (webView.CoreWebView2 is not null)
        {
            return;
        }

        Directory.CreateDirectory(WebViewUserDataFolder);
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: WebViewUserDataFolder);
        await webView.EnsureCoreWebView2Async(environment);
        var core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 初始化失败。");
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
    }

    private static async Task NavigateAsync(WebView2 webView, string url, CancellationToken cancellationToken)
    {
        var core = webView.CoreWebView2!;
        var current = core.Source ?? string.Empty;
        if (current.TrimEnd('/').Equals(url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, CoreWebView2NavigationCompletedEventArgs args)
        {
            webView.NavigationCompleted -= Handler;
            if (args.IsSuccess)
            {
                tcs.TrySetResult();
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException($"页面加载失败: {args.WebErrorStatus}"));
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

    private static async Task WaitForDocumentReadyAsync(WebView2 webView, CancellationToken cancellationToken)
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

        await Task.Delay(1000, cancellationToken);
    }

    private static bool NeedsLogin(string currentUrl)
    {
        return currentUrl.Contains("login", StringComparison.OrdinalIgnoreCase)
               || currentUrl.Contains("passport", StringComparison.OrdinalIgnoreCase)
               || currentUrl.Contains("auth", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task TryAutoLoginAsync(WebView2 webView, CredentialSettings settings, CancellationToken cancellationToken)
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

    private static async Task<T?> FetchUsageDataAsync<T>(WebView2 webView, string relativeUrl)
    {
        var core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 未初始化。");
        var cookies = await core.CookieManager.GetCookiesAsync(UsageUrl);
        var token = cookies.FirstOrDefault(cookie => cookie.Name.Equals("bigmodel_token_production", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
        var organization = await ExecuteJsonAsync<string>(webView, "localStorage.getItem('Bigmodel-Organization') || ''");
        var project = await ExecuteJsonAsync<string>(webView, "localStorage.getItem('Bigmodel-Project') || ''");
        WidgetTrace.Write($"Fetch headers tokenLen={token.Length}, org={organization}, project={project}");

        var script = $$"""
            (() => {
              const xhr = new XMLHttpRequest();
              xhr.open('GET', {{JsonSerializer.Serialize(relativeUrl)}}, false);
              xhr.withCredentials = true;
              xhr.setRequestHeader('Authorization', {{JsonSerializer.Serialize(Uri.UnescapeDataString(token))}});
              xhr.setRequestHeader('Bigmodel-Organization', {{JsonSerializer.Serialize(organization)}});
              xhr.setRequestHeader('Bigmodel-Project', {{JsonSerializer.Serialize(project)}});
              xhr.setRequestHeader('Set-Language', 'zh');
              xhr.send();
              return xhr.responseText;
            })();
            """;
        var raw = await ExecuteJsonAsync<string>(webView, script);
        WidgetTrace.Write($"Fetch {relativeUrl} => {raw[..Math.Min(raw.Length, 240)]}");
        return JsonSerializer.Deserialize<T>(raw);
    }

    private static async Task<string> GetLocationAsync(WebView2 webView)
    {
        return await ExecuteJsonAsync<string>(webView, "window.location.href");
    }

    private static async Task<string> GetBodyTextAsync(WebView2 webView)
    {
        return await ExecuteJsonAsync<string>(webView, "(() => document.body ? document.body.innerText : '')()");
    }

    private static async Task<T> ExecuteJsonAsync<T>(WebView2 webView, string script)
    {
        var raw = await webView.ExecuteScriptAsync(script);
        if (typeof(T) == typeof(string))
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return (T)(object)(doc.RootElement.GetString() ?? string.Empty);
            }

            return (T)(object)doc.RootElement.GetRawText();
        }

        var value = JsonSerializer.Deserialize<T>(raw);
        if (value is null)
        {
            throw new InvalidOperationException("脚本返回空结果。");
        }
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
            Label = item.Type == "TIME_LIMIT" ? "每5小时使用额度" : "MCP 每月额度",
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

            var label = $"{item.ModelName} 消耗";
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

        return series.OrderByDescending(item => item.TotalValue).ToList();
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
