using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SoftwareDock.App.Shell;

namespace SoftwareDock.App.Platform;

/// <summary>
/// A system-wide shortcut, delivered through a message-only window of our own.
/// </summary>
/// <remarks>
/// Owning the window keeps the hotkey independent of the dock's window, which is hidden most of
/// the time and recreated by the UI framework at will. The window lives on the UI thread, so
/// <see cref="Pressed"/> is raised there.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x5D0C;
    private static readonly ConcurrentDictionary<nint, GlobalHotkey> Instances = new();

    private readonly nint _className;
    private readonly nint _instance;
    private nint _hwnd;
    private bool _disposed;

    private GlobalHotkey(HotkeyGesture gesture)
    {
        Gesture = gesture;
        _instance = NativeMethods.GetModuleHandle(null);
        _className = Marshal.StringToHGlobalUni($"SoftwareDock.Hotkey.{Environment.ProcessId}");

        var windowClass = new NativeMethods.WndClassEx
        {
            Size = (uint)sizeof(NativeMethods.WndClassEx),
            WndProc = &WndProc,
            Instance = _instance,
            ClassName = _className,
        };
        if (NativeMethods.RegisterClassEx(windowClass) == 0)
        {
            Marshal.FreeHGlobal(_className);
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastPInvokeError()}).");
        }

        _hwnd = NativeMethods.CreateWindowEx(0, _className, string.Empty, 0, 0, 0, 0, 0, NativeMethods.HwndMessage, 0, _instance, 0);
        if (_hwnd == 0)
        {
            Cleanup();
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()}).");
        }

        Instances[_hwnd] = this;
    }

    public event EventHandler? Pressed;

    public HotkeyGesture Gesture { get; }

    /// <summary>
    /// Registers <paramref name="gesture"/>. Returns <see langword="null"/> when another application
    /// already owns the combination — common and not an error; the tray still works.
    /// </summary>
    public static GlobalHotkey? TryRegister(HotkeyGesture gesture)
    {
        var hotkey = new GlobalHotkey(gesture);
        if (NativeMethods.RegisterHotKey(hotkey._hwnd, HotkeyId, (uint)gesture.Modifiers | NativeMethods.ModNoRepeat, (uint)gesture.VirtualKey))
        {
            return hotkey;
        }

        hotkey.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != 0)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            Instances.TryRemove(_hwnd, out _);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        Cleanup();
    }

    private void Cleanup()
    {
        NativeMethods.UnregisterClass(_className, _instance);
        Marshal.FreeHGlobal(_className);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == NativeMethods.WmHotkey && wParam == HotkeyId && Instances.TryGetValue(hwnd, out var hotkey))
        {
            try
            {
                hotkey.Pressed?.Invoke(hotkey, EventArgs.Empty);
            }
#pragma warning disable CA1031 // An exception escaping an unmanaged callback terminates the process.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                System.Diagnostics.Trace.TraceError($"Hotkey handler failed: {ex}");
            }

            return 0;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
