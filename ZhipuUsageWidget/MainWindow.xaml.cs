using System.Collections.ObjectModel;
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
    private const double ChartHeight = 120;
    private const double ChartTop = 4;

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
    private readonly Dictionary<string, (UsageSnapshot Snapshot, DateTimeOffset CachedAt)> _snapshotCache = [];

    private CredentialSettings _settings = CredentialSettings.CreateDefault();
    private UsageSnapshot? _currentSnapshot;
    private DateTime _currentRangeStart = DateTime.Today.AddDays(-6);
    private DateTime _currentRangeEnd = DateTime.Today;
    private bool _isRefreshing;
    private bool _isChartMode = true;
    private bool _hasPendingRefresh;

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
        _refreshTimer.Tick += RefreshTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();
        NormalizeSelectedModels();
        PositionWindow();
        ApplyPersistedRange();
        ApplyCollapsedState();
        ConfigureRefreshTimer();
        await RefreshUsageAsync();
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
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
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
        var days = _settings.RangeDays is 1 or 7 or 30 ? _settings.RangeDays : 7;
        ApplyDatesForPreset(days);
    }

    private void ApplyDatesForPreset(int days)
    {
        _currentRangeStart = DateTime.Today.AddDays(-(days - 1));
        _currentRangeEnd = DateTime.Today;
        _settings.RangeDays = days;
        UpdateRangeButtons(days == 1 ? "1" : days == 30 ? "30" : "7");
    }

    private void UpdateRangeButtons(string mode)
    {
        TodayRangeButton.Foreground = mode == "1" ? Brushes.White : CreateBrush("#94A3B8");
        WeekRangeButton.Foreground = mode == "7" ? Brushes.White : CreateBrush("#94A3B8");
        MonthRangeButton.Foreground = mode == "30" ? Brushes.White : CreateBrush("#94A3B8");
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _snapshotCache.Clear();
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

                var start = _currentRangeStart;
                var end = _currentRangeEnd;

                var cacheKey = $"{start:yyyy-MM-dd}_{end:yyyy-MM-dd}";
                if (_snapshotCache.TryGetValue(cacheKey, out var entry) && (DateTimeOffset.Now - entry.CachedAt).TotalMinutes < 5)
                {
                    _currentSnapshot = entry.Snapshot;
                }
                else
                {
                    _currentSnapshot = await _automationService.RefreshUsageAsync(HiddenWebView, _settings, start, end, CancellationToken.None);
                    _snapshotCache[cacheKey] = (_currentSnapshot, DateTimeOffset.Now);
                }

                RebuildUi();
            } while (_hasPendingRefresh);
        }
        finally
        {
            _isRefreshing = false;
        }
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
        LegendPanel.Children.Clear();
        var displayed = GetDisplayedSeries();

        if (displayed.Count == 0)
        {
            LegendDockPanel.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var (series, index) in displayed.Take(3).Select((item, i) => (item, i)))
        {
            var text = new TextBlock { Margin = new Thickness(0, 0, 10, 0), Foreground = Brushes.White, FontSize = 10 };
            text.Inlines.Add(new Run("● ") { Foreground = CreateBrush(Palette[index % Palette.Length]) });
            text.Inlines.Add(new Run(series.Label));
            LegendPanel.Children.Add(text);
        }

        if (displayed.Count > 3)
        {
            LegendPanel.Children.Add(new TextBlock
            {
                Text = $"+{displayed.Count - 3}",
                Foreground = CreateBrush("#94A3B8"),
                FontSize = 10,
            });
        }

        ChartValueTextBlock.Text = displayed.Count == 1
            ? displayed[0].DisplayValue
            : string.Empty;

        LegendDockPanel.Visibility = Visibility.Visible;
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

        var seriesList = GetDisplayedSeries();
        var allPoints = seriesList.SelectMany(item => item.Points).ToList();
        if (!seriesList.Any() || allPoints.Count == 0)
        {
            CollapsedHintTextBlock.Visibility = Visibility.Visible;
            CollapsedHintTextBlock.Text = "没有可绘制的数据";
            ChartValueTextBlock.Text = "--";
            return;
        }

        CollapsedHintTextBlock.Visibility = Visibility.Collapsed;
        var maxValue = Math.Max(1, allPoints.Max(point => point.Value));
        for (var i = 0; i < 4; i++)
        {
            var ratio = i / 3d;
            var y = ChartTop + (1 - ratio) * ChartHeight;
            _gridLines.Add(new ChartGridLineViewModel { Top = y, Label = FormatYAxis(maxValue * ratio) });
        }

        for (var i = 0; i < seriesList.Count; i++)
        {
            var points = new PointCollection(seriesList[i].Points.Select(point =>
                new Point(
                    ChartLayoutHelper.ComputePointX(point.Time, _currentRangeStart, _currentRangeEnd),
                    ChartTop + (1 - point.Value / maxValue) * ChartHeight)));

            _chartSeries.Add(new ChartSeriesViewModel
            {
                Label = seriesList[i].Label,
                DisplayValue = seriesList[i].DisplayValue,
                Stroke = CreateBrush(Palette[i % Palette.Length]),
                Points = points,
            });
        }

        foreach (var label in ChartLayoutHelper.BuildAxisLabels(_currentRangeStart, _currentRangeEnd))
        {
            _axisLabels.Add(new ChartAxisLabelViewModel
            {
                Left = label.Left,
                Text = label.Text,
            });
        }
    }

    private void UpdatePanels()
    {
        var collapsed = _settings.IsCollapsed;

        // Collapsed: only show expand + gear buttons
        ExpandButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ModeButtonsBorder.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
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

        ChartModeButton.Foreground = _isChartMode ? Brushes.White : CreateBrush("#94A3B8");
        SummaryModeButton.Foreground = !_isChartMode ? Brushes.White : CreateBrush("#94A3B8");
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
        _snapshotCache.Clear();
        await RefreshUsageAsync();
    }

    private async void CollapseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsCollapsed = !_settings.IsCollapsed;
        await _settingsService.SaveAsync(_settings);
        ApplyCollapsedState();
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
        if (x < ChartLayoutHelper.ChartLeft || x > ChartLayoutHelper.ChartLeft + ChartLayoutHelper.ChartWidth)
        {
            HideTrackOverlay();
            return;
        }

        // Vertical indicator line
        TrackVerticalLine.X1 = x;
        TrackVerticalLine.X2 = x;
        TrackVerticalLine.Visibility = Visibility.Visible;

        // Convert X to time
        var hitTime = ChartLayoutHelper.ComputeTimeFromX(x, _currentRangeStart, _currentRangeEnd);

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
            var px = ChartLayoutHelper.ComputePointX(closest.Time, _currentRangeStart, _currentRangeEnd);
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
        if (tooltipX + 130 > ChartLayoutHelper.ChartLeft + ChartLayoutHelper.ChartWidth)
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
