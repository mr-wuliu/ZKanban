using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ZhipuUsageWidget;

internal static class SimpleMessageBox
{
    public static async Task ShowAsync(Window owner, string message, string title = "提示")
    {
        var button = new Button
        {
            Content = "确定",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(16, 6),
        };
        var dialog = new Window
        {
            Title = title,
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = WindowDecorations.BorderOnly,
            Background = new SolidColorBrush(Color.Parse("#101826")),
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#E2E8F0")),
                    },
                    button,
                },
            },
        };
        button.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
