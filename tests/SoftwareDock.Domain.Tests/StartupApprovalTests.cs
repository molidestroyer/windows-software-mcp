using SoftwareDock.Domain.Startup;

namespace SoftwareDock.Domain.Tests;

public class StartupApprovalTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    public void IsEnabled_ReadsTaskManagersFormat(byte[]? value, bool expected) =>
        Assert.Equal(expected, StartupApproval.IsEnabled(value));

    [Fact]
    public void Disabled_RecordsWhen_AndRoundTrips()
    {
        var when = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

        var value = StartupApproval.Disabled(when);

        Assert.Equal(12, value.Length);
        Assert.False(StartupApproval.IsEnabled(value));
        Assert.Equal(when.ToFileTime(), BitConverter.ToInt64(value, 4));
        Assert.True(StartupApproval.IsEnabled(StartupApproval.Enabled()));
    }

    [Fact]
    public void Id_IsLocationAndName()
    {
        Assert.Equal("UserRun:Discord", new StartupItem("Discord", "x", StartupLocation.UserRun, null, true).Id);
    }
}
