using DoViFixer.Application.Settings;
using Microsoft.Extensions.Logging;
using WpfFoundation.Notifications;

namespace DoViFixer.App.Presentation.Application;

/// <summary>
/// Sends the notifications <c>OperationFeedback</c> asks for only while the user's "Show a notification when an
/// operation finishes" setting is on. The setting is read when a notification is due, so a change applies at once.
/// </summary>
public sealed class CompletionNotifications(Lazy<INotificationService> notifications, SettingsService settings, ILogger<CompletionNotifications> logger) : INotificationService
{
    public void Register(string appId, string displayName, string? iconPath) => notifications.Value.Register(appId, displayName, iconPath);

    public void Unregister() => notifications.Value.Unregister();

    public void Show(string title, string message, NotificationKind kind = NotificationKind.Information) =>
        _ = ShowIfEnabledAsync(title, message, kind);

    /// <summary>Sends the notification when the setting is on; a failure is logged, never thrown.</summary>
    public async Task ShowIfEnabledAsync(string title, string message, NotificationKind kind)
    {
        try
        {
            var current = await settings.ReadAsync(CancellationToken.None);
            if (current.ShowCompletionNotifications)
            {
                notifications.Value.Show(title, message, kind);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not show the notification for {Operation}", title);
        }
    }
}
