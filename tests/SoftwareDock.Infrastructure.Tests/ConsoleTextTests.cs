using SoftwareDock.Infrastructure.Processes;

namespace SoftwareDock.Infrastructure.Tests;

public class ConsoleTextTests
{
    [Theory]
    [InlineData("   - \r   \\ \rFound Git", "Found Git")]
    [InlineData("\u001b[32mSuccessfully installed\u001b[0m\r", "Successfully installed")]
    public void CleanLine_KeepsWhatTheTerminalWouldShow(string raw, string expected)
    {
        Assert.Equal(expected, ConsoleText.CleanLine(raw));
    }

    [Theory]
    [InlineData("  - ")]
    [InlineData("  ██████████▒▒▒▒▒▒  ")]
    [InlineData("")]
    public void IsNoise_SpinnersAndBars(string line) => Assert.True(ConsoleText.IsNoise(line));

    [Fact]
    public void IsNoise_ProgressWithNumbers_IsKept() => Assert.False(ConsoleText.IsNoise("  ██████  12.0 MB / 40.0 MB"));
}
