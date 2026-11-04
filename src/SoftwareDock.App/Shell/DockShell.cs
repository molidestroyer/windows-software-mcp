using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoftwareDock.App.Platform;
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
    private readonly Dictionary<string, WindowIcon> _icons = [];

    private TrayIcon? _tray;
    private IDisposable? _hotkey;

    public DockShell(
        DockWindow window,
        DockViewModel viewModel,
        IStartupRegistration startup,
        IOptions<DockOptions> options,
        ILogger<DockShell> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _window = window;
        _viewModel = viewModel;
        _startup = startup;
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

        // Warm the cache in the background so the first open is instant.
        _ = _viewModel.LoadAsync();

        if (showPanel)
        {
            _window.ShowDock();
        }
    }

    public void ShowDock() => _window.ShowDock();

    public void Dispose()
    {
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
            new NativeMenuItemSeparator(),
            exit,
        ];
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The hotkey {Hotkey} is taken by another application; open the dock from the tray instead")]
    private partial void LogHotkeyTaken(string hotkey);
}
