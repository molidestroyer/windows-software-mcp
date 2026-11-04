using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Tests;

public class ApplicationKeyTests
{
    [Theory]
    [InlineData(InstallScope.Machine, RegistryView.Native64, @"ARP\Machine\X64\{K}")]
    [InlineData(InstallScope.Machine, RegistryView.Wow32, @"ARP\Machine\X86\{K}")]
    [InlineData(InstallScope.User, RegistryView.Native64, @"ARP\User\X64\{K}")]
    public void ForRegistry_UsesTheShapeWinGetPrints(InstallScope scope, RegistryView view, string expected)
    {
        Assert.Equal(expected, ApplicationKey.ForRegistry(scope, view, "{K}").Value);
    }

    [Fact]
    public void Equality_IsCaseInsensitive()
    {
        Assert.Equal(ApplicationKey.Parse(@"ARP\Machine\X64\{abc}"), ApplicationKey.Parse(@"arp\machine\x64\{ABC}"));
    }
}
