using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SoftwareDock.App.Settings;

public interface IUserSettingsStore
{
    event EventHandler<UserSettings>? Changed;

    UserSettings Current { get; }

    void Update(Func<UserSettings, UserSettings> change);
}

/// <summary>Keeps settings in memory only: demo mode and tests.</summary>
public sealed class InMemoryUserSettingsStore(UserSettings? initial = null) : IUserSettingsStore
{
    public event EventHandler<UserSettings>? Changed;

    public UserSettings Current { get; private set; } = initial ?? new UserSettings();

    public void Update(Func<UserSettings, UserSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Current = change(Current);
        Changed?.Invoke(this, Current);
    }
}

/// <summary><c>%LOCALAPPDATA%\SoftwareDock\settings.json</c>. A corrupt file falls back to defaults instead of crashing the tray.</summary>
public sealed partial class JsonFileUserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<JsonFileUserSettingsStore> _logger;
    private readonly Lock _lock = new();

    public JsonFileUserSettingsStore(ILogger<JsonFileUserSettingsStore> logger)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftwareDock", "settings.json"), logger)
    {
    }

    public JsonFileUserSettingsStore(string path, ILogger<JsonFileUserSettingsStore> logger)
    {
        _path = path;
        _logger = logger;
        Current = Load();
    }

    public event EventHandler<UserSettings>? Changed;

    public UserSettings Current { get; private set; }

    public void Update(Func<UserSettings, UserSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_lock)
        {
            Current = change(Current);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(Current, Json));
            }
            catch (IOException ex)
            {
                LogSaveFailed(ex, _path);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogSaveFailed(ex, _path);
            }
        }

        Changed?.Invoke(this, Current);
    }

    private UserSettings Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path), Json) ?? new UserSettings() : new UserSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(ex, _path);
            return new UserSettings();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path}; using default settings")]
    private partial void LogLoadFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save {Path}")]
    private partial void LogSaveFailed(Exception exception, string path);
}
