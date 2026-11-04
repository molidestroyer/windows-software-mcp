namespace SoftwareDock.Domain.Packages;

/// <summary>
/// A package manager identifier (<c>Microsoft.PowerToys</c>, <c>9NBLGGH4NNS1</c>,
/// <c>ARP\Machine\X64\{…}</c>).
/// </summary>
/// <remarks>
/// Identifiers reach us from two untrusted places — a console table and a language model — so
/// the invariants that keep them safe to hand to a process live here, in one place:
/// no control characters, no quotes, and no leading <c>-</c> that would turn an id into a switch
/// (<c>--override</c> is an argument-injection vector on <c>winget install</c>).
/// </remarks>
public sealed record PackageId
{
    /// <summary>WinGet ends a column it had to cut with this character.</summary>
    public const char TruncationMarker = '…';

    private PackageId(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// <see langword="true"/> when the console cut the identifier short. A truncated id cannot be
    /// acted on — it would match nothing, or worse, match by prefix something else.
    /// </summary>
    public bool IsTruncated => Value.EndsWith(TruncationMarker);

    public static PackageId Parse(string value)
    {
        if (!TryParse(value, out var id, out var error))
        {
            throw new ArgumentException(error, nameof(value));
        }

        return id;
    }

    public static bool TryParse(string? value, out PackageId id, out string error)
    {
        id = null!;
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            error = "A package id cannot be empty.";
            return false;
        }

        if (candidate.Length > 256)
        {
            error = "A package id cannot be longer than 256 characters.";
            return false;
        }

        if (candidate[0] == '-')
        {
            error = $"'{candidate}' is not a package id: ids never start with '-'.";
            return false;
        }

        foreach (var c in candidate)
        {
            if (char.IsControl(c) || c is '"' or '\'' or '`')
            {
                error = $"'{candidate}' contains characters a package id cannot have.";
                return false;
            }
        }

        id = new PackageId(candidate);
        error = string.Empty;
        return true;
    }

    public bool Equals(PackageId? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;
}
