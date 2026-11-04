namespace SoftwareDock.App.Shell;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

/// <summary>A global shortcut parsed from configuration (<c>Win+Shift+I</c>), in Win32 terms.</summary>
/// <param name="Modifiers">The <c>MOD_*</c> flags <c>RegisterHotKey</c> takes; the values match.</param>
/// <param name="VirtualKey">The Win32 virtual-key code.</param>
/// <param name="Display">Normalized text for menus and tooltips.</param>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey, string Display)
{
    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            // A bare key as a global hotkey would steal it from every application.
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" or "META" => HotkeyModifiers.Windows,
                _ => (HotkeyModifiers?)null,
            };
            if (modifier is null || modifiers.HasFlag(modifier.Value))
            {
                return false;
            }

            modifiers |= modifier.Value;
        }

        if (!TryParseKey(parts[^1], out var virtualKey, out var keyName))
        {
            return false;
        }

        var display = string.Join('+', new[]
        {
            modifiers.HasFlag(HotkeyModifiers.Windows) ? "Win" : null,
            modifiers.HasFlag(HotkeyModifiers.Control) ? "Ctrl" : null,
            modifiers.HasFlag(HotkeyModifiers.Alt) ? "Alt" : null,
            modifiers.HasFlag(HotkeyModifiers.Shift) ? "Shift" : null,
            keyName,
        }.OfType<string>());

        gesture = new HotkeyGesture(modifiers, virtualKey, display);
        return true;
    }

    private static bool TryParseKey(string key, out int virtualKey, out string name)
    {
        var upper = key.ToUpperInvariant();
        name = upper;
        virtualKey = 0;

        if (upper.Length == 1 && upper[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        {
            virtualKey = upper[0];
            return true;
        }

        if (upper.Length is 2 or 3 && upper[0] == 'F' && int.TryParse(upper[1..], out var f) && f is >= 1 and <= 24)
        {
            virtualKey = 0x70 + f - 1;
            return true;
        }

        (virtualKey, name) = upper switch
        {
            "SPACE" => (0x20, "Space"),
            "INSERT" or "INS" => (0x2D, "Insert"),
            "HOME" => (0x24, "Home"),
            "END" => (0x23, "End"),
            _ => (0, upper),
        };
        return virtualKey != 0;
    }
}
