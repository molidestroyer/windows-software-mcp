using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SoftwareDock.Application.Startup;
using SoftwareDock.Domain.Startup;

namespace SoftwareDock.App.ViewModels;

/// <summary>The Startup tab: what Windows starts at sign-in, each with Task Manager's switch.</summary>
public sealed partial class StartupPageViewModel(StartupManager startup, Action<string, bool> status) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _enabledCount;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];

    public string Summary => $"{EnabledCount} of {Items.Count} start with Windows";

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entries = await startup.ListAsync(CancellationToken.None);
            Items.Clear();
            foreach (var entry in entries)
            {
                Items.Add(new StartupItemViewModel(entry, this));
            }

            EnabledCount = Items.Count(i => i.IsEnabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            status($"Could not read the startup apps: {ex.Message}", true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal async Task ToggleAsync(StartupItemViewModel item, bool enabled)
    {
        var result = await startup.SetEnabledAsync(item.Entry.Item, enabled, CancellationToken.None);
        status(result.Message, !result.IsSuccess);
        if (result.IsSuccess)
        {
            item.Accept(enabled);
        }
        else
        {
            item.Revert();
        }

        EnabledCount = Items.Count(i => i.IsEnabled);
    }
}

public sealed partial class StartupItemViewModel : ObservableObject
{
    private readonly StartupPageViewModel _owner;
    private bool _committed;
    private bool _reverting;

    [ObservableProperty]
    private bool _isEnabled;

    public StartupItemViewModel(StartupEntry entry, StartupPageViewModel owner)
    {
        Entry = entry;
        _owner = owner;
        _isEnabled = _committed = entry.Item.IsEnabled;
        Initials = Avatar.Initials(entry.DisplayName);
        AvatarBrush = Avatar.BrushFor(entry.DisplayName);
    }

    public StartupEntry Entry { get; private set; }

    public string Name => Entry.DisplayName;

    public string Initials { get; }

    public IBrush AvatarBrush { get; }

    public string Subtitle => Entry.Item.Location switch
    {
        StartupLocation.UserRun or StartupLocation.UserStartupFolder => "This user",
        _ => "All users · needs administrator to change",
    } + (Entry.Application is null || Entry.Application.Name == Entry.Item.Name ? string.Empty : $"  ·  {Entry.Item.Name}");

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_reverting && value != _committed)
        {
            _ = _owner.ToggleAsync(this, value);
        }
    }

    internal void Accept(bool enabled)
    {
        _committed = enabled;
        Entry = Entry with { Item = Entry.Item with { IsEnabled = enabled } };
    }

    internal void Revert()
    {
        _reverting = true;
        IsEnabled = _committed;
        _reverting = false;
    }
}
