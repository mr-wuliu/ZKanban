using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZhipuUsageWidget.Models;
using ZhipuUsageWidget.Services;

namespace ZhipuUsageWidget;

public partial class MainWindow : Window
{
    private const double ExpandedWidth = 450;
    private const double ExpandedHeight = 274;
    private const double ChartHeight = 124;
    private const double ChartTop = 0;

    private static readonly string[] Palette = ["#67A6FF", "#FF9D4B", "#60E6B2", "#D38FFF", "#FFD166", "#7DD3FC"];

    private readonly ObservableCollection<ChartSeriesViewModel> _chartSeries = [];
    private readonly ObservableCollection<ChartAxisLabelViewModel> _axisLabels = [];
    private readonly ObservableCollection<ChartGridLineViewModel> _gridLines = [];
    private readonly ObservableCollection<MetricTileViewModel> _summaryItems = [];
    private readonly ObservableCollection<TrackDotViewModel> _trackDots = [];
    private readonly ObservableCollection<TrackSeriesItemViewModel> _trackSeriesItems = [];
    private readonly LocalSettingsService _settingsService = new();
    private readonly BigModelAutomationService _automationService = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly HashSet<string> _selectedCurveLabels = [];
    private readonly UsageHistoryService _historyService = new();

    private CredentialSettings _settings = CredentialSettings.CreateDefault();
    private UsageSnapshot? _currentSnapshot;
    private DateTime _currentRangeStart = DateTime.Today.AddDays(-6);
    private DateTime _currentRangeEnd = DateTime.Today;
    private bool _isRefreshing;
    private bool _isChartMode = true;
    private bool _hasPendingRefresh;
    private double _chartWidth = 370;
    private DateTime? _chartDataEnd;
    private bool _historyLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Width = ExpandedWidth;
        Height = ExpandedHeight;
        SeriesItemsControl.ItemsSource = _chartSeries;
        AxisLabelsItemsControl.ItemsSource = _axisLabels;
        GridLinesItemsControl.ItemsSource = _gridLines;
        SummaryItemsControl.ItemsSource = _summaryItems;
        TrackDotsItemsControl.ItemsSource = _trackDots;
        TrackSeriesItemsControl.ItemsSource = _trackSeriesItems;
        Loaded += MainWindow_Loaded;
        ChartCanvas.Loaded += ChartCanvas_Loaded;
        _refreshTimer.Tick += RefreshTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();
        _historyService.LoadFromDisk();
        _historyLoaded = true;
        NormalizeSelectedModels();
        PositionWindow();
        ApplyPersistedRange();
        ApplyCollapsedState();
        ConfigureRefreshTimer();
        await RefreshUsageAsync();
    }

    private void ChartCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        ChartCanvas.SizeChanged += ChartCanvas_SizeChanged;
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && _chartSeries.Count > 0)
        {
            RenderChart();
        }
    }

    private void NormalizeSelectedModels()
    {
        _selectedCurveLabels.Clear();

        if (_settings.SelectedModels.Remove("Token 消耗总量"))
        {
            _settings.SelectedModels.Insert(0, "总用量");
        }

        foreach (var label in _settings.SelectedModels)
        {
            _selectedCurveLabels.Add(label);
        }

        if (_selectedCurveLabels.Count == 0)
        {
            _selectedCurveLabels.Add("总用量");
        }
    }

    private void PositionWindow()
    {
        PositionToTopRight();
    }

    private void PositionToTopRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Top + 16;
    }

    private void ConfigureRefreshTimer()
    {
        _refreshTimer.Stop();
        _refreshTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(_settings.RefreshIntervalMinutes, 1, 240));
        _refreshTimer.Start();
    }

    private void ApplyPersistedRange()
    {
        var days = _settings.RangeDays is 1 or 7 or 30 or 60 ? _settings.RangeDays : 7;
        ApplyDatesForPreset(days);
    }

    private void ApplyDatesForPreset(int days)
    {
        _currentRangeStart = DateTime.Today.AddDays(-(days - 1));
        _currentRangeEnd = DateTime.Today;
        _settings.RangeDays = days;
        UpdateRangeButtons(days == 1 ? "1" : days == 7 ? "7" : days == 30 ? "30" : "60");
    }

    private void UpdateRangeButtons(string mode)
    {
        TodayRangeButton.Foreground = mode == "1" ? Brushes.White : CreateBrush("#94A3B8");
        TodayRangeButton.Background = mode == "1" ? CreateBrush("#1C3650") : Brushes.Transparent;
        WeekRangeButton.Foreground = mode == "7" ? Brushes.White : CreateBrush("#94A3B8");
        WeekRangeButton.Background = mode == "7" ? CreateBrush("#1C3650") : Brushes.Transparent;
        MonthRangeButton.Foreground = mode == "30" ? Brushes.White : CreateBrush("#94A3B8");
        MonthRangeButton.Background = mode == "30" ? CreateBrush("#1C3650") : Brushes.Transparent;
        Month60RangeButton.Foreground = mode == "60" ? Brushes.White : CreateBrush("#94A3B8");
        Month60RangeButton.Background = mode == "60" ? CreateBrush("#1C3650") : Brushes.Transparent;
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshUsageAsync();
    }

    private async Task RefreshUsageAsync()
    {
        if (_isRefreshing)
        {
            _hasPendingRefresh = true;
            return;
        }

        _isRefreshing = true;

        try
        {
            do
            {
                _hasPendingRefresh = false;

                var today = DateTime.Today;
                var todayDate = DateOnly.FromDateTime(today);
                var rangeDays = _settings.RangeDays;

                // Step 1: Always fetch today's data (establishes login session)
                var todaySnapshot = await _automationService.RefreshUsageAsync(
                    HiddenWebView, _settings, today, today, CancellationToken.None);

                if (todaySnapshot.IsLoggedIn)
                {
                    // Update today's daily total in history
                    UpdateHistoryFromSnapshot(todayDate, todaySnapshot);

                    // Step 2: Backfill gaps for multi-day views
                    if (rangeDays > 1)
                    {
                        var historyStart = todayDate.AddDays(-(rangeDays - 1));
                        var yesterday = todayDate.AddDays(-1);
                        var gaps = _historyService.FindGaps(historyStart, yesterday);
                        foreach (var (gapStart, gapEnd) in gaps)
                        {
                            var records = await _automationService.FetchDailyUsageRecordsAsync(
                                HiddenWebView, _settings, gapStart, gapEnd, CancellationToken.None);
                            foreach (var (date, models) in records)
                            {
                                _historyService.UpdateDay(date, models);
                            }
                        }
                    }

                    _historyService.SaveToDisk();
                }

                // Step 3: Build the display snapshot
                if (rangeDays == 1)
                {
                    // Today view: use hourly data directly
                    _currentSnapshot = todaySnapshot;
                }
                else
                {
                    // Multi-day view: merge history cache into snapshot
                    var historyStart = todayDate.AddDays(-(rangeDays - 1));
                    _currentSnapshot = BuildMergedSnapshot(historyStart, todayDate, todaySnapshot, rangeDays);
                }

                RebuildUi();
            } while (_hasPendingRefresh);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void UpdateHistoryFromSnapshot(DateOnly date, UsageSnapshot snapshot)
    {
        var models = new List<ModelDailyUsage>();
        foreach (var series in snapshot.ModelUsages)
        {
            models.Add(new ModelDailyUsage
            {
                Name = series.Label,
                Tokens = series.TotalValue,
            });
        }

        _historyService.UpdateDay(date, models);
    }

    private UsageSnapshot BuildMergedSnapshot(DateOnly start, DateOnly today, UsageSnapshot todaySnapshot, int rangeDays)
    {
        var cachedRecords = _historyService.GetRange(start, today);
        var recordsByDate = cachedRecords.ToDictionary(r => DateOnly.Parse(r.Date, CultureInfo.InvariantCulture), r => r);

        // Collect all model names across all days
        var modelNames = new HashSet<string>();
        foreach (var record in cachedRecords)
        {
            foreach (var model in record.Models)
            {
                modelNames.Add(model.Name);
            }
        }

        // Build per-model series with daily points (fill gaps with 0)
        var seriesList = new List<ModelUsageSeries>();
        foreach (var modelName in modelNames)
        {
            var points = new List<UsageSeriesPoint>();
            for (var d = start; d <= today; d = d.AddDays(1))
            {
                var tokens = 0d;
                if (recordsByDate.TryGetValue(d, out var record))
                {
                    var usage = record.Models.FirstOrDefault(m => m.Name == modelName);
                    tokens = usage?.Tokens ?? 0d;
                }

                points.Add(new UsageSeriesPoint
                {
                    Time = d.ToDateTime(TimeOnly.MinValue),
                    Value = tokens,
                });
            }

            var total = points.Sum(p => p.Value);
            seriesList.Add(new ModelUsageSeries
            {
                Label = modelName,
                DisplayValue = FormatCompactNumberStatic(total),
                TotalValue = total,
                Points = points,
            });
        }

        seriesList = seriesList.OrderByDescending(s => s.TotalValue).ToList();

        // Build total series
        var totalPoints = new List<UsageSeriesPoint>();
        for (var d = start; d <= today; d = d.AddDays(1))
        {
            var dayTotal = 0d;
            if (recordsByDate.TryGetValue(d, out var rec))
            {
                dayTotal = rec.Models.Sum(m => m.Tokens);
            }

            totalPoints.Add(new UsageSeriesPoint
            {
                Time = d.ToDateTime(TimeOnly.MinValue),
                Value = dayTotal,
            });
        }

        var totalSeries = new ModelUsageSeries
        {
            Label = "总用量",
            DisplayValue = FormatCompactNumberStatic(totalPoints.Sum(p => p.Value)),
            TotalValue = totalPoints.Sum(p => p.Value),
            Points = totalPoints,
        };

        return new UsageSnapshot
        {
            Status = todaySnapshot.Status,
            LastUpdated = DateTimeOffset.Now,
            Quotas = todaySnapshot.Quotas,
            ModelUsages = seriesList,
            TotalUsageSeries = totalSeries,
            RawSummary = todaySnapshot.RawSummary,
            IsLoggedIn = todaySnapshot.IsLoggedIn,
            CurrentUrl = todaySnapshot.CurrentUrl,
            RangeDays = rangeDays,
        };
    }

    private static string FormatCompactNumberStatic(double value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000d:0.##} M";
        if (value >= 1_000) return $"{value / 1_000d:0.##} K";
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void RebuildUi()
    {
        if (_currentSnapshot is null)
        {
            return;
        }

        SyncSelectedCurvesWithSnapshot();
        RebuildCurveContextMenu();

        _summaryItems.Clear();
        foreach (var quota in _currentSnapshot.Quotas)
        {
            _summaryItems.Add(new MetricTileViewModel { Label = quota.Label, Value = quota.PercentText });
        }

        if (_currentSnapshot.TotalUsageSeries is not null)
        {
            _summaryItems.Add(new MetricTileViewModel { Label = _currentSnapshot.TotalUsageSeries.Label, Value = _currentSnapshot.TotalUsageSeries.DisplayValue });
        }

        foreach (var item in _currentSnapshot.ModelUsages.Take(4))
        {
            _summaryItems.Add(new MetricTileViewModel { Label = item.Label, Value = item.DisplayValue });
        }

        RenderChart();
        RenderLegend();
        UpdatePanels();
    }

    private void SyncSelectedCurvesWithSnapshot()
    {
        var valid = GetAvailableSeries().Select(item => item.Label).ToHashSet();
        _selectedCurveLabels.RemoveWhere(label => !valid.Contains(label));
        if (_selectedCurveLabels.Count == 0)
        {
            _selectedCurveLabels.Add("总用量");
        }

        _settings.SelectedModels = _selectedCurveLabels.ToList();
    }

    private IReadOnlyList<ModelUsageSeries> GetAvailableSeries()
    {
        if (_currentSnapshot is null)
        {
            return [];
        }

        var series = new List<ModelUsageSeries>();
        if (_currentSnapshot.TotalUsageSeries is not null)
        {
            series.Add(_currentSnapshot.TotalUsageSeries);
        }

        series.AddRange(_currentSnapshot.ModelUsages);
        return series;
    }

    private void RebuildCurveContextMenu()
    {
        CurveContextMenu.Items.Clear();
        foreach (var series in GetAvailableSeries())
        {
            var item = new MenuItem
            {
                Header = series.Label,
                IsCheckable = true,
                IsChecked = _selectedCurveLabels.Contains(series.Label),
                StaysOpenOnClick = true,
                Tag = series.Label,
            };
            item.Checked += CurveMenuItem_Checked;
            item.Unchecked += CurveMenuItem_Unchecked;
            CurveContextMenu.Items.Add(item);
        }
    }

    private void CurveMenuItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string label)
        {
            return;
        }

        _selectedCurveLabels.Add(label);
        PersistCurveSelection();
        RenderChart();
        RenderLegend();
    }

    private void CurveMenuItem_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string label)
        {
            return;
        }

        if (_selectedCurveLabels.Count <= 1 && _selectedCurveLabels.Contains(label))
        {
            menuItem.IsChecked = true;
            return;
        }

        _selectedCurveLabels.Remove(label);
        PersistCurveSelection();
        RenderChart();
        RenderLegend();
    }

    private void PersistCurveSelection()
    {
        _settings.SelectedModels = _selectedCurveLabels.ToList();
        _ = _settingsService.SaveAsync(_settings);
    }

    private void RenderLegend()
    {
    }

    private IReadOnlyList<ModelUsageSeries> GetDisplayedSeries()
    {
        return GetAvailableSeries()
            .Where(item => _selectedCurveLabels.Contains(item.Label))
            .ToList();
    }

    private void RenderChart()
    {
        _chartSeries.Clear();
        _axisLabels.Clear();
        _gridLines.Clear();

        var canvasWidth = ChartCanvas.ActualWidth;
        _chartWidth = canvasWidth > ChartLayoutHelper.ChartLeft + 40
            ? canvasWidth - ChartLayoutHelper.ChartLeft
            : 370;

        // Ensure the inner Canvas of AxisLabelsItemsControl has enough width for all labels
        AxisLabelsItemsControl.Width = ChartLayoutHelper.ChartLeft + _chartWidth;

        var seriesList = GetDisplayedSeries();
        var allPoints = seriesList.SelectMany(item => item.Points).ToList();
        if (!seriesList.Any() || allPoints.Count == 0)
        {
            _chartDataEnd = null;
            CollapsedHintTextBlock.Visibility = Visibility.Visible;
            CollapsedHintTextBlock.Text = "没有可绘制的数据";
            return;
        }

        CollapsedHintTextBlock.Visibility = Visibility.Collapsed;
        _chartDataEnd = allPoints.Max(point => point.Time);
        var maxValue = Math.Max(1, allPoints.Max(point => point.Value));
        var gridRight = ChartLayoutHelper.ChartLeft + _chartWidth;
        XAxisLine.X2 = gridRight;
        for (var i = 0; i < 4; i++)
        {
            var ratio = i / 3d;
            var y = ChartTop + (1 - ratio) * ChartHeight;
            _gridLines.Add(new ChartGridLineViewModel { Top = y, Right = gridRight, Label = FormatYAxis(maxValue * ratio) });
        }

        for (var i = 0; i < seriesList.Count; i++)
        {
            var points = new PointCollection(seriesList[i].Points.Select(point =>
                new Point(
                    ChartLayoutHelper.ComputePointX(point.Time, _currentRangeStart, _currentRangeEnd, _chartWidth, _chartDataEnd),
                    ChartTop + (1 - point.Value / maxValue) * ChartHeight)));

            _chartSeries.Add(new ChartSeriesViewModel
            {
                Label = seriesList[i].Label,
                DisplayValue = seriesList[i].DisplayValue,
                Stroke = CreateBrush(Palette[i % Palette.Length]),
                Points = points,
            });
        }

        foreach (var label in ChartLayoutHelper.BuildAxisLabels(_currentRangeStart, _currentRangeEnd, _chartWidth, _chartDataEnd))
        {
            _axisLabels.Add(new ChartAxisLabelViewModel
            {
                Left = label.Left,
                Text = label.Text,
                Width = label.Width,
            });
        }
    }

    private void UpdatePanels()
    {
        var collapsed = _settings.IsCollapsed;

        // Toggle icon: expanded → show minimize (▲), collapsed → show expand (▼)
        ExpandButtonIcon.Data = collapsed
            ? Geometry.Parse("M4,6 L8,2 L12,6")
            : Geometry.Parse("M4,2 L8,6 L12,2");

        ChartModeButton.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SummaryModeButton.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CurveSelectorButton.Visibility = collapsed || !_isChartMode ? Visibility.Collapsed : Visibility.Visible;
        RangeButtonsBorder.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        // Content area
        ChartPanel.Visibility = collapsed || !_isChartMode ? Visibility.Collapsed : Visibility.Visible;
        SummaryPanel.Visibility = collapsed || _isChartMode ? Visibility.Collapsed : Visibility.Visible;
        CollapsedHintTextBlock.Visibility = collapsed ? Visibility.Collapsed : CollapsedHintTextBlock.Visibility;
        ContentRow.Height = collapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        // Window sizing
        if (collapsed)
        {
            SizeToContent = SizeToContent.WidthAndHeight;
            MinWidth = 0;
            Width = double.NaN;
            Height = double.NaN;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
            MinWidth = 410;
            Width = ExpandedWidth;
            Height = double.NaN;
        }

        ChartModeIcon.Stroke = _isChartMode ? Brushes.White : CreateBrush("#94A3B8");
        SummaryModeIcon.Stroke = !_isChartMode ? Brushes.White : CreateBrush("#94A3B8");
    }

    private void ApplyCollapsedState()
    {
        UpdatePanels();
        PositionWindow();
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged)
        {
            Left += sizeInfo.PreviousSize.Width - sizeInfo.NewSize.Width;
        }
    }

    private static string FormatYAxis(double value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000:0.#}M";
        if (value >= 1_000) return $"{value / 1_000:0.#}K";
        return value.ToString("0");
    }

    private async void TodayRangeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyDatesForPreset(1);
        await _settingsService.SaveAsync(_settings);
        await RefreshUsageAsync();
    }

    private async void WeekRangeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyDatesForPreset(7);
        await _settingsService.SaveAsync(_settings);
        await RefreshUsageAsync();
    }

    private async void MonthRangeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyDatesForPreset(30);
        await _settingsService.SaveAsync(_settings);
        await RefreshUsageAsync();
    }

    private async void Month60RangeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyDatesForPreset(60);
        await _settingsService.SaveAsync(_settings);
        await RefreshUsageAsync();
    }

    private void CurveSelectorButton_Click(object sender, RoutedEventArgs e)
    {
        CurveContextMenu.PlacementTarget = CurveSelectorButton;
        CurveContextMenu.IsOpen = true;
        RemovePopupChrome(CurveContextMenu);
    }

    private void ChartModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isChartMode = true;
        UpdatePanels();
    }

    private void SummaryModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isChartMode = false;
        UpdatePanels();
    }

    private void ChartModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _isChartMode = true;
        UpdatePanels();
    }

    private void SummaryModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _isChartMode = false;
        UpdatePanels();
    }

    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        GearContextMenu.PlacementTarget = GearButton;
        GearContextMenu.IsOpen = true;
        RemovePopupChrome(GearContextMenu);
    }

    private static void RemovePopupChrome(ContextMenu menu)
    {
        menu.Dispatcher.BeginInvoke(() =>
        {
            if (menu.Parent is Popup popup)
            {
                popup.AllowsTransparency = true;
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void RefreshMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshUsageAsync();
    }

    private async void CollapseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsCollapsed = !_settings.IsCollapsed;
        await _settingsService.SaveAsync(_settings);
        UpdatePanels();
    }

    private async void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings, _automationService) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _settings = dialog.Settings;
        NormalizeSelectedModels();
        ConfigureRefreshTimer();
        ApplyPersistedRange();
        await _settingsService.SaveAsync(_settings);
        await RefreshUsageAsync();
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_chartSeries.Count == 0 || _currentSnapshot is null) return;

        var pos = e.GetPosition(ChartCanvas);
        var x = pos.X;

        // Check if within chart area
        if (x < ChartLayoutHelper.ChartLeft || x > ChartLayoutHelper.ChartLeft + _chartWidth)
        {
            HideTrackOverlay();
            return;
        }

        // Vertical indicator line
        TrackVerticalLine.X1 = x;
        TrackVerticalLine.X2 = x;
        TrackVerticalLine.Visibility = Visibility.Visible;

        // Convert X to time
        var hitTime = ChartLayoutHelper.ComputeTimeFromX(x, _currentRangeStart, _currentRangeEnd, _chartWidth, _chartDataEnd);

        // Find closest point in each series
        _trackDots.Clear();
        _trackSeriesItems.Clear();

        var seriesList = GetDisplayedSeries();
        var allPoints = seriesList.SelectMany(s => s.Points).ToList();
        var maxValue = Math.Max(1, allPoints.Max(p => p.Value));

        for (var i = 0; i < seriesList.Count; i++)
        {
            var series = seriesList[i];
            var closest = series.Points.OrderBy(p => Math.Abs((p.Time - hitTime).TotalSeconds)).First();
            var px = ChartLayoutHelper.ComputePointX(closest.Time, _currentRangeStart, _currentRangeEnd, _chartWidth, _chartDataEnd);
            var py = ChartTop + (1 - closest.Value / maxValue) * ChartHeight;

            var stroke = CreateBrush(Palette[i % Palette.Length]);
            _trackDots.Add(new TrackDotViewModel { Left = px - 3.5, Top = py - 3.5, Stroke = stroke });
            _trackSeriesItems.Add(new TrackSeriesItemViewModel { Stroke = stroke, Text = $"{series.Label}: {closest.Value:0.#}" });
        }

        TrackDotsItemsControl.Visibility = Visibility.Visible;

        // Tooltip
        TrackTimeText.Text = hitTime.ToString("MM-dd HH:mm");
        TrackSeriesItemsControl.ItemsSource = _trackSeriesItems;
        TrackToolTip.Visibility = Visibility.Visible;

        // Position tooltip - prefer right of cursor, flip left if overflow
        var tooltipX = x + 10;
        var tooltipY = pos.Y - 10;
        if (tooltipX + 130 > ChartLayoutHelper.ChartLeft + _chartWidth)
            tooltipX = x - 140;
        Canvas.SetLeft(TrackToolTip, tooltipX);
        Canvas.SetTop(TrackToolTip, Math.Max(4, tooltipY));
    }

    private void ChartCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        HideTrackOverlay();
    }

    private void HideTrackOverlay()
    {
        TrackVerticalLine.Visibility = Visibility.Collapsed;
        TrackDotsItemsControl.Visibility = Visibility.Collapsed;
        TrackToolTip.Visibility = Visibility.Collapsed;
    }
}

public sealed class TrackDotViewModel
{
    public double Left { get; init; }
    public double Top { get; init; }
    public System.Windows.Media.Brush Stroke { get; init; } = System.Windows.Media.Brushes.White;
}

public sealed class TrackSeriesItemViewModel
{
    public System.Windows.Media.Brush Stroke { get; init; } = System.Windows.Media.Brushes.White;
    public string Text { get; init; } = string.Empty;
}
