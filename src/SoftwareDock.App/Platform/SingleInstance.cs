namespace SoftwareDock.App.Platform;

/// <summary>
/// One dock per user session. A second launch (Start menu, startup entry) does not open a second
/// tray icon: it asks the running instance to show its panel and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string Name = @"Local\SoftwareDock.{6C1E2B57-6F5B-4C38-9E0E-5A2F3D9B7C41}";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle? showSignal)
    {
        _mutex = mutex;
        _showSignal = showSignal;
    }

    /// <summary>Returns the instance guard, or <see langword="null"/> after signalling the one already running.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, Name + ".Mutex", out var createdNew);
        EventWaitHandle? signal = OperatingSystem.IsWindows()
            ? new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".Show")
            : null;

        if (createdNew)
        {
            return new SingleInstance(mutex, signal);
        }

        signal?.Set();
        signal?.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onShowRequested"/> (on a pool thread) whenever another launch asks.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        if (_showSignal is not null)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showSignal?.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
