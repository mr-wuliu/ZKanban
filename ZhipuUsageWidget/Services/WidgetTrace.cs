using System.IO;

namespace ZhipuUsageWidget.Services;

public static class WidgetTrace
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZhipuUsageWidget");

    private static readonly string LogPath = Path.Combine(LogDirectory, "widget.log");

    public static string CurrentLogPath => LogPath;

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
