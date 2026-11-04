using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoftwareDock.App.Platform;
using SoftwareDock.App.Services;
using SoftwareDock.App.Settings;
using SoftwareDock.App.ViewModels;

namespace SoftwareDock.App.Shell;

/// <summary>
/// Everything that is not the panel itself: tray icon and menu, global hotkey, start-with-Windows.
/// </summary>
/// <remarks>
/// The tray icon doubles as a status light, like CasaAdminDock's: green when everything is up to
/// date, amber when updates are waiting, blue while an operation runs, red when the inventory
/// could not be read.
/// </remarks>
public sealed partial class DockShell : IDisposable
{
    private readonly DockWindow _window;
    private readonly DockViewModel _viewModel;
    private readonly IStartupRegistration _startup;
    private readonly DockOptions _options;
    private readonly ILogger<DockShell> _logger;
    private readonly IUserSettingsStore _settings;
    private readonly IAppUpdater _updater;
    private readonly AutoBackup _autoBackup;
    private readonly Dictionary<string, WindowIcon> _icons = [];
    private readonly CancellationTokenSource _lifetime = new();

    private TrayIcon? _tray;
    private IDisposable? _hotkey;

    public DockShell(
        DockWindow window,
        DockViewModel viewModel,
        IStartupRegistration startup,
        IUserSettingsStore settings,
        IAppUpdater updater,
        AutoBackup autoBackup,
        IOptions<DockOptions> options,
        ILogger<DockShell> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _window = window;
        _viewModel = viewModel;
        _startup = startup;
        _settings = settings;
        _updater = updater;
        _autoBackup = autoBackup; // Held so it lives (and keeps listening for inventory changes) as long as the shell.
        _options = options.Value;
        _logger = logger;
    }

    public void Start(bool showPanel)
    {
        _tray = new TrayIcon
        {
            Icon = Icon("tray-ok"),
            ToolTipText = "Software Dock",
            Menu = BuildMenu(),
        };
        _tray.Clicked += (_, _) => _window.ToggleDock();
        TrayIcon.SetIcons(Avalonia.Application.Current!, [_tray]);

        _viewModel.PropertyChanged += (_, _) => UpdateTrayState();
        RegisterHotkey();

        // Warm the cache in the background so the first open is instant (and the backup current).
        _ = _viewModel.LoadAsync();
        _ = CheckSoftwareUpdatesPeriodicallyAsync(_lifetime.Token);
        _ = CheckAppUpdatesPeriodicallyAsync(_lifetime.Token);

        if (showPanel)
        {
            _window.ShowDock();
        }
    }

    public void ShowDock() => _window.ShowDock();

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        foreach (var icon in _icons.Values)
        {
            (icon as IDisposable)?.Dispose();
        }
    }

    private NativeMenu BuildMenu()
    {
        var open = new NativeMenuItem(_viewModel.HotkeyText is { } hotkey ? $"Open Software Dock    {hotkey}" : "Open Software Dock");
        open.Click += (_, _) => _window.ShowDock();

        var refresh = new NativeMenuItem("Refresh");
        refresh.Click += (_, _) => _viewModel.RefreshCommand.Execute(null);

        var copyMcp = new NativeMenuItem("Copy MCP config") { ToolTip = "Copies the .mcp.json snippet that connects an AI agent to this PC's software" };
        copyMcp.Click += async (_, _) =>
        {
            if (_window.Clipboard is { } clipboard)
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, McpConnection.ClientConfig());
                _viewModel.StatusMessage = "MCP config copied — paste it into .mcp.json.";
            }
        };

        var startWithWindows = new NativeMenuItem("Start with Windows")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _startup.IsEnabled,
            IsEnabled = _startup.IsSupported,
        };
        startWithWindows.Click += (_, _) =>
        {
            _startup.SetEnabled(!_startup.IsEnabled);
            startWithWindows.IsChecked = _startup.IsEnabled;
        };

        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) =>
        {
            _viewModel.Page = ViewModels.DockPage.Settings;
            _window.ShowDock();
        };

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };

        return
        [
            open,
            new NativeMenuItemSeparator(),
            refresh,
            copyMcp,
            new NativeMenuItemSeparator(),
            startWithWindows,
            settings,
            new NativeMenuItemSeparator(),
            exit,
        ];
    }

    /// <summary>
    /// Rebuilds the inventory every few hours so the tray turns amber when updates appear, without
    /// anyone opening the panel. Skipped while the panel is open: it refreshes on its own then.
    /// </summary>
    private async Task CheckSoftwareUpdatesPeriodicallyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var hours = Math.Max(1, _settings.Current.SoftwareUpdateCheckHours);
                await Task.Delay(TimeSpan.FromHours(hours), cancellationToken);
                if (_settings.Current.CheckForSoftwareUpdates && !_window.IsVisible)
                {
                    await _viewModel.LoadAsync(forceRefresh: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Checks GitHub for a new dock release shortly after start and then twice a day.</summary>
    private async Task CheckAppUpdatesPeriodicallyAsync(CancellationToken cancellationToken)
    {
        if (!_updater.IsSupported)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_settings.Current.AutoUpdateApp)
                {
                    try
                    {
                        if (await _updater.CheckAsync(cancellationToken) is { } update)
                        {
                            await _updater.DownloadAsync(update, null, cancellationToken);
                            _viewModel.OfferAppUpdate(update);
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
                    {
                        LogAppUpdateFailed(ex);
                    }
                }

                await Task.Delay(TimeSpan.FromHours(12), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RegisterHotkey()
    {
        if (!OperatingSystem.IsWindows() || !HotkeyGesture.TryParse(_options.Hotkey, out var gesture))
        {
            return;
        }

        var hotkey = GlobalHotkey.TryRegister(gesture!);
        if (hotkey is null)
        {
            LogHotkeyTaken(gesture!.Display);
            return;
        }

        hotkey.Pressed += (_, _) => _window.ToggleDock();
        _hotkey = hotkey;
    }

    private void UpdateTrayState()
    {
        if (_tray is null)
        {
            return;
        }

        var (icon, state) = _viewModel switch
        {
            { IsBusy: true } => ("tray-busy", _viewModel.StatusMessage ?? "Working…"),
            { StatusIsError: true, InstalledCount: 0 } => ("tray-error", _viewModel.StatusMessage ?? "Could not read the installed software"),
            { HasUpdates: true } => ("tray-updates", _viewModel.Summary),
            _ => ("tray-ok", _viewModel.Summary),
        };

        _tray.Icon = Icon(icon);
        _tray.ToolTipText = Truncate($"Software Dock — {state}", 127);
    }

    private WindowIcon Icon(string name)
    {
        if (!_icons.TryGetValue(name, out var icon))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://SoftwareDock/Assets/{name}.png"));
            icon = new WindowIcon(stream);
            _icons[name] = icon;
        }

        return icon;
    }

    // Shell_NotifyIcon truncates tooltips at 128 characters.
    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Checking for a new Software Dock release failed")]
    private partial void LogAppUpdateFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The hotkey {Hotkey} is taken by another application; open the dock from the tray instead")]
    private partial void LogHotkeyTaken(string hotkey);
}
