using SoftwareDock.Infrastructure.Registry;

namespace SoftwareDock.Infrastructure.Tests;

public class RegistryInstallDateTests
{
    [Theory]
    [InlineData("20240315", 2024, 3, 15)]
    public void ParseInstallDate_RegistryFormat(string value, int year, int month, int day)
    {
#pragma warning disable CA1416 // Pure parsing helper; the class is Windows-only for its registry access.
        Assert.Equal(new DateOnly(year, month, day), RegistryInstalledApplicationSource.ParseInstallDate(value));
#pragma warning restore CA1416
    }

    [Theory]
    [InlineData(null)]
    [InlineData("15/03/2024")]
    [InlineData("2024-03-15")]
    public void ParseInstallDate_AnythingElse_IsUnknown(string? value)
    {
#pragma warning disable CA1416
        Assert.Null(RegistryInstalledApplicationSource.ParseInstallDate(value));
#pragma warning restore CA1416
    }
}
