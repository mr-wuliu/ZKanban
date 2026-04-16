using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ZKanban.Services;

namespace ZKanban;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Force dark variant so FluentTheme controls use light foreground
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        // Apply saved theme BEFORE creating window
        var themeName = LocalSettingsService.LoadThemeNameSync();
        ThemeManager.ApplyTheme(themeName);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 左键点击托盘图标 → 切换主窗口显示/隐藏
    /// </summary>
    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        ToggleMainWindow();
    }

    private void OnTrayShowHide(object? sender, EventArgs e)
    {
        ToggleMainWindow();
    }

    private void OnTrayExit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ToggleMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var window = desktop.MainWindow;
        if (window is null)
            return;

        if (window.IsVisible)
        {
            window.Hide();
        }
        else
        {
            window.Show();
            window.Activate();
        }
    }
}
