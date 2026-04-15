using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ZhipuUsageWidget.Models;
using ZhipuUsageWidget.Services;

namespace ZhipuUsageWidget;

public partial class SettingsWindow : Window
{
    private readonly BigModelAutomationService _automationService;
    private readonly ObservableCollection<ModelVisibilityOption> _modelOptions = [];
    private readonly TextBox _passwordTextBox;

    public CredentialSettings Settings { get; }

    public SettingsWindow(CredentialSettings currentSettings, BigModelAutomationService automationService)
    {
        InitializeComponent();
        _automationService = automationService;
        _passwordTextBox = this.FindControl<TextBox>("PasswordTextBox") ?? throw new InvalidOperationException("PasswordTextBox not found");
        Settings = currentSettings.Clone();
        UsernameTextBox.Text = Settings.Username;
        _passwordTextBox.Text = Settings.Password;
        RefreshIntervalTextBox.Text = Settings.RefreshIntervalMinutes.ToString();
        AutoLoginCheckBox.IsChecked = Settings.AutoLogin;
        ModelOptionsItemsControl.ItemsSource = _modelOptions;
        Loaded += SettingsWindow_Loaded;
    }

    private async void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        await RefreshLoginStatusAsync();
        await LoadModelOptionsAsync();
    }

    private void ApplyValues()
    {
        Settings.Username = UsernameTextBox.Text?.Trim() ?? string.Empty;
        Settings.Password = _passwordTextBox.Text ?? string.Empty;
        Settings.AutoLogin = AutoLoginCheckBox.IsChecked == true;
        if (int.TryParse(RefreshIntervalTextBox.Text, out var minutes))
        {
            Settings.RefreshIntervalMinutes = minutes;
        }

        Settings.SelectedModels = [.. _modelOptions.Where(item => item.IsSelected).Select(item => item.Label)];
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
            var state = await BigModelAutomationService.GetLoginStateAsync(hiddenWebView, Settings, Settings.AutoLogin, CancellationToken.None);
            LoginStatusTextBlock.Text = state.IsLoggedIn ? "已登录" : "未登录";
            probeWindow.Close();
        }
        catch (Exception)
        {
            LoginStatusTextBlock.Text = "检查失败";
        }
    }

    private static Window CreateProbeWindow(out NativeWebView hiddenWebView)
    {
        var probeWindow = new Window
        {
            Width = 1,
            Height = 1,
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent,
        };
        hiddenWebView = new NativeWebView();
        hiddenWebView.EnvironmentRequested += BigModelAutomationService.ConfigureWebViewEnvironment;
        probeWindow.Content = hiddenWebView;
        probeWindow.Show();
        return probeWindow;
    }

    private async void CheckStatusButton_Click(object? sender, RoutedEventArgs e)
    {
        await RefreshLoginStatusAsync();
    }

    private async void LogoutButton_Click(object? sender, RoutedEventArgs e)
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
            await SimpleMessageBox.ShowAsync(this, ex.Message, "清空登录态失败");
        }
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshIntervalTextBox.Text, out var interval) || interval is < 1 or > 240)
        {
            await SimpleMessageBox.ShowAsync(this, "刷新间隔必须是 1 到 240 之间的整数。", "提示");
            return;
        }

        ApplyValues();
        Settings.RefreshIntervalMinutes = interval;
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
