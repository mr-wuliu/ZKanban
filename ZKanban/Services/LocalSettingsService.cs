using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZKanban.Models;

namespace ZKanban.Services;

public sealed class LocalSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public LocalSettingsService()
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZKanban");
        Directory.CreateDirectory(appDirectory);
        _settingsPath = Path.Combine(appDirectory, "settings.json");
    }

    public async Task<CredentialSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            return CredentialSettings.CreateDefault();
        }

        await using var stream = File.OpenRead(_settingsPath);
        var persisted = await JsonSerializer.DeserializeAsync<PersistedSettings>(stream, JsonOptions);
        if (persisted is null)
        {
            return CredentialSettings.CreateDefault();
        }

        return new CredentialSettings
        {
            Username = persisted.Username ?? string.Empty,
            Password = Decrypt(persisted.PasswordProtected),
            RefreshIntervalMinutes = persisted.RefreshIntervalMinutes <= 0 ? 10 : persisted.RefreshIntervalMinutes,
            AutoLogin = persisted.AutoLogin,
            RangeDays = persisted.RangeDays is 1 or 7 or 30 or 60 ? persisted.RangeDays : 7,
            IsCollapsed = persisted.IsCollapsed,
            SelectedModels = persisted.SelectedModels?.Count > 0
                ? [.. persisted.SelectedModels]
                : CredentialSettings.CreateDefault().SelectedModels,
        };
    }

    public async Task SaveAsync(CredentialSettings settings)
    {
        var persisted = new PersistedSettings
        {
            Username = settings.Username,
            PasswordProtected = Encrypt(settings.Password),
            RefreshIntervalMinutes = settings.RefreshIntervalMinutes,
            AutoLogin = settings.AutoLogin,
            RangeDays = settings.RangeDays,
            IsCollapsed = settings.IsCollapsed,
            SelectedModels = [.. settings.SelectedModels],
        };

        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, persisted, JsonOptions);
    }

    private static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string? protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(protectedText);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class PersistedSettings
    {
        public string? Username { get; set; }

        public string? PasswordProtected { get; set; }

        public int RefreshIntervalMinutes { get; set; } = 10;

        public bool AutoLogin { get; set; } = true;

        public int RangeDays { get; set; } = 7;

        public DateTime? CustomRangeStart { get; set; }

        public DateTime? CustomRangeEnd { get; set; }

        public bool IsCollapsed { get; set; }

        public List<string>? SelectedModels { get; set; }
    }
}
