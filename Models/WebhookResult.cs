namespace WorkflowTracker.Models;

public sealed record WebhookResult(bool ShouldUninstall = false, RemoteSettingsPatch? SettingsPatch = null,
    string? SettingsError = null)
{
    public static readonly WebhookResult Accepted = new();
    public static readonly WebhookResult UninstallRequested = new(ShouldUninstall: true);
}
