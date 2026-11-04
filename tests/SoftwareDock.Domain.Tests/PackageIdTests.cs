using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Domain.Tests;

public class PackageIdTests
{
    [Theory]
    [InlineData("Microsoft.PowerToys")]
    [InlineData("9NBLGGH4NNS1")]
    [InlineData(@"ARP\Machine\X64\{23170F69-40C1-2702-2301-000001000000}")]
    [InlineData(@"ARP\User\X64\Notepad++ (64-bit)")]
    public void TryParse_ValidIds_Succeed(string value)
    {
        Assert.True(PackageId.TryParse(value, out var id, out _));
        Assert.Equal(value, id.Value);
    }

    [Theory]
    [InlineData("--override")]
    [InlineData("-h")]
    [InlineData("Foo\" --override \"x")]
    [InlineData("Foo\nBar")]
    [InlineData("")]
    public void TryParse_ArgumentInjectionAndGarbage_IsRejected(string value)
    {
        Assert.False(PackageId.TryParse(value, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsTruncated_IdCutByTheConsole_IsDetected()
    {
        Assert.True(PackageId.Parse("Microsoft.VisualStudio.2022.Build…").IsTruncated);
        Assert.False(PackageId.Parse("Microsoft.VisualStudio.2022.BuildTools").IsTruncated);
    }

    [Fact]
    public void Equality_IsCaseInsensitive()
    {
        Assert.Equal(PackageId.Parse("Git.Git"), PackageId.Parse("git.git"));
    }
}
