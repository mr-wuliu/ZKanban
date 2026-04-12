namespace ZhipuUsageWidget.Models;

public sealed class CredentialSettings
{
    private static readonly string[] DefaultModels =
    [
        "总用量",
        "GLM-5.1 消耗",
        "GLM-5 消耗",
        "GLM-5-TURBO 消耗",
    ];

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int RefreshIntervalMinutes { get; set; } = 10;

    public bool AutoLogin { get; set; } = true;

    public int RangeDays { get; set; } = 7;

    public bool IsCollapsed { get; set; }

    public List<string> SelectedModels { get; set; } = [.. DefaultModels];

    public static CredentialSettings CreateDefault() => new();

    public CredentialSettings Clone()
    {
        return new CredentialSettings
        {
            Username = Username,
            Password = Password,
            RefreshIntervalMinutes = RefreshIntervalMinutes,
            AutoLogin = AutoLogin,
            RangeDays = RangeDays,
            IsCollapsed = IsCollapsed,
            SelectedModels = [.. SelectedModels],
        };
    }
}
