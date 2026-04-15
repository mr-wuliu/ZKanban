using Avalonia;
using System;
using System.IO;

namespace ZKanban;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        MigrateLegacyDataDirs();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// One-time migration: renames old ZhipuUsageWidget data directories to ZKanban.
    /// Runs silently — any failure is ignored so the app still starts normally.
    /// </summary>
    private static void MigrateLegacyDataDirs()
    {
        MigrateDir(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZhipuUsageWidget"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZKanban"));

        MigrateDir(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZhipuUsageWidget"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZKanban"));
    }

    private static void MigrateDir(string oldPath, string newPath)
    {
        try
        {
            if (!Directory.Exists(oldPath) || Directory.Exists(newPath))
            {
                return;
            }

            Directory.Move(oldPath, newPath);
        }
        catch
        {
            // Ignore — app will create a fresh directory on first use
        }
    }
}
