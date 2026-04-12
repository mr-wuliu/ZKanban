using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ZhipuUsageWidget.Models;
using ZhipuUsageWidget.Services;

namespace ZhipuUsageWidget;

public partial class SettingsWindow : Window
{
    private readonly BigModelAutomationService _automationService;
    private readonly ObservableCollection<ModelVisibilityOption> _modelOptions = [];

    public CredentialSettings Settings { get; }

    public SettingsWindow(CredentialSettings currentSettings, BigModelAutomationService automationService)
    {
        InitializeComponent();
        _automationService = automationService;
        Settings = currentSettings.Clone();
        UsernameTextBox.Text = Settings.Username;
        PasswordTextBox.Password = Settings.Password;
        RefreshIntervalTextBox.Text = Settings.RefreshIntervalMinutes.ToString();
        AutoLoginCheckBox.IsChecked = Settings.AutoLogin;
        ModelOptionsItemsControl.ItemsSource = _modelOptions;
        Loaded += SettingsWindow_Loaded;
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshLoginStatusAsync();
        await LoadModelOptionsAsync();
    }

    private void ApplyValues()
    {
        Settings.Username = UsernameTextBox.Text.Trim();
        Settings.Password = PasswordTextBox.Password;
        Settings.AutoLogin = AutoLoginCheckBox.IsChecked == true;
        if (int.TryParse(RefreshIntervalTextBox.Text, out var minutes))
        {
            Settings.RefreshIntervalMinutes = minutes;
        }

        Settings.SelectedModels = _modelOptions.Where(item => item.IsSelected).Select(item => item.Label).ToList();
    }

    private async Task LoadModelOptionsAsync()
    {
        ApplyValues();
        try
        {
            var probeWindow = CreateProbeWindow(out var hiddenWebView);
            var availableModels = await _automationService.FetchAvailableModelsAsync(hiddenWebView, Settings, CancellationToken.None);
            probeWindow.Close();

            var selected = Settings.SelectedModels.Count > 0 ? Settings.SelectedModels : availableModels;
            _modelOptions.Clear();
            foreach (var model in availableModels.Distinct())
            {
                _modelOptions.Add(new ModelVisibilityOption
                {
                    Label = model,
                    IsSelected = selected.Contains(model),
                });
            }
        }
        catch
        {
            if (_modelOptions.Count > 0)
            {
                return;
            }

            foreach (var model in Settings.SelectedModels.Distinct())
            {
                _modelOptions.Add(new ModelVisibilityOption
                {
                    Label = model,
                    IsSelected = true,
                });
            }
        }
    }

    private async Task RefreshLoginStatusAsync()
    {
        ApplyValues();
        LoginStatusTextBlock.Text = "检查中...";
        try
        {
            var probeWindow = CreateProbeWindow(out var hiddenWebView);
            var state = await _automationService.GetLoginStateAsync(hiddenWebView, Settings, Settings.AutoLogin, CancellationToken.None);
            LoginStatusTextBlock.Text = state.IsLoggedIn ? "已登录" : "未登录";
            probeWindow.Close();
        }
        catch (Exception ex)
        {
            LoginStatusTextBlock.Text = "检查失败";
        }
    }

    private static Window CreateProbeWindow(out Microsoft.Web.WebView2.Wpf.WebView2 hiddenWebView)
    {
        var probeWindow = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
        };
        hiddenWebView = new Microsoft.Web.WebView2.Wpf.WebView2();
        probeWindow.Content = hiddenWebView;
        probeWindow.Show();
        return probeWindow;
    }

    private async void CheckStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLoginStatusAsync();
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var probeWindow = CreateProbeWindow(out var hiddenWebView);
            await _automationService.LogoutAsync(hiddenWebView, CancellationToken.None);
            probeWindow.Close();
            await RefreshLoginStatusAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "清空登录态失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshIntervalTextBox.Text, out var interval) || interval is < 1 or > 240)
        {
            MessageBox.Show(this, "刷新间隔必须是 1 到 240 之间的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyValues();
        Settings.RefreshIntervalMinutes = interval;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
