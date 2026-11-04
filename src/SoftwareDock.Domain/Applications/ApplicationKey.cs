namespace SoftwareDock.Domain.Applications;

/// <summary>
/// Stable identity of an installed application.
/// </summary>
/// <remarks>
/// Registry entries use the exact shape WinGet prints for local packages
/// (<c>ARP\Machine\X64\{ProductCode}</c>), so a WinGet <c>list</c> row and the registry entry it
/// describes compare equal without any guessing. Comparison is ordinal and case-insensitive,
/// like the registry itself.
/// </remarks>
public sealed record ApplicationKey
{
    private ApplicationKey(string value) => Value = value;

    public string Value { get; }

    public static ApplicationKey ForRegistry(InstallScope scope, RegistryView view, string subKeyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyName);
        if (scope == InstallScope.Unknown)
        {
            throw new ArgumentException("A registry entry always belongs to a known scope.", nameof(scope));
        }

        var architecture = view switch
        {
            RegistryView.Native64 => "X64",
            RegistryView.Wow32 => "X86",
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, null),
        };

        return new ApplicationKey($@"ARP\{scope}\{architecture}\{subKeyName}");
    }

    /// <summary>Wraps an identifier that already has the canonical shape (e.g. one WinGet printed).</summary>
    public static ApplicationKey Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ApplicationKey(value.Trim());
    }

    public bool IsRegistryEntry => Value.StartsWith(@"ARP\", StringComparison.OrdinalIgnoreCase);

    public bool Equals(ApplicationKey? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;
}

/// <summary>Which registry view an uninstall entry was read from.</summary>
public enum RegistryView
{
    Native64,
    Wow32,
}
