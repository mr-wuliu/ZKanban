namespace ZhipuUsageWidget.Models;

public sealed class LoginStateInfo
{
    public bool IsLoggedIn { get; init; }

    public string CurrentUrl { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
