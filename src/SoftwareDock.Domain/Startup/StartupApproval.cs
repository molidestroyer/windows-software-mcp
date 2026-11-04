namespace SoftwareDock.Domain.Startup;

/// <summary>
/// The format of <c>Explorer\StartupApproved</c> values — how Task Manager disables a startup app
/// without deleting it, and how this dock does too, so both stay in agreement.
/// </summary>
/// <remarks>
/// 12 bytes. The low bit of byte 0 is the state: even (<c>0x02</c>, <c>0x06</c>) enabled, odd
/// (<c>0x03</c>, <c>0x07</c>) disabled; bytes 4–11 hold the FILETIME it was disabled. A missing
/// value means enabled.
/// </remarks>
public static class StartupApproval
{
    public const int Length = 12;

    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] Enabled() => [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public static byte[] Disabled(DateTimeOffset when)
    {
        var value = new byte[Length];
        value[0] = 0x03;
        BitConverter.GetBytes(when.ToFileTime()).CopyTo(value, 4);
        return value;
    }
}
