using System.Runtime.Versioning;
using Microsoft.Toolkit.Uwp.Notifications;

namespace SoftwareDock.App.Services;

/// <summary>What clicking a notification opens.</summary>
public enum NotificationTarget
{
    Dock,
    Updates,
    Settings,
}

public sealed record DockNotification(string Title, string Body, NotificationTarget Target = NotificationTarget.Dock, string? ActionLabel = null);

/// <summary>Port: tell the user something while the panel is closed.</summary>
public interface INotifier
{
    /// <summary>Raised on the UI thread's behalf when the user clicks a notification.</summary>
    event EventHandler<NotificationTarget>? Activated;

    void Notify(DockNotification notification);
}

/// <summary>
/// Windows toast notifications for an unpackaged app. The compat layer registers an
/// AppUserModelID with the dock's name and icon under HKCU, so toasts work for portable copies
/// and Velopack installs alike, and clicking one reaches the running process.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class ToastNotifier : INotifier, IDisposable
{
    private const string TargetArgument = "target";

    public ToastNotifier() => ToastNotificationManagerCompat.OnActivated += OnActivated;

    public event EventHandler<NotificationTarget>? Activated;

    public void Notify(DockNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var builder = new ToastContentBuilder()
            .AddArgument(TargetArgument, notification.Target.ToString())
            .AddText(notification.Title)
            .AddText(notification.Body);

        if (notification.ActionLabel is not null)
        {
            builder.AddButton(new ToastButton().SetContent(notification.ActionLabel).AddArgument(TargetArgument, notification.Target.ToString()));
        }

        builder.Show();
    }

    public void Dispose() => ToastNotificationManagerCompat.OnActivated -= OnActivated;

    private void OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var arguments = ToastArguments.Parse(e.Argument);
        var target = arguments.TryGetValue(TargetArgument, out var value) && Enum.TryParse<NotificationTarget>(value, out var parsed)
            ? parsed
            : NotificationTarget.Dock;
        Activated?.Invoke(this, target);
    }
}

/// <summary>Demo mode and tests: remembers notifications instead of showing them.</summary>
public sealed class RecordingNotifier : INotifier
{
    private readonly List<DockNotification> _sent = [];

    public event EventHandler<NotificationTarget>? Activated;

    public IReadOnlyList<DockNotification> Sent => _sent;

    public void Notify(DockNotification notification) => _sent.Add(notification);

    public void Click(NotificationTarget target) => Activated?.Invoke(this, target);
}
