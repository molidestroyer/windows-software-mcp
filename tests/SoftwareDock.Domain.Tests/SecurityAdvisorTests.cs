using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Security;

namespace SoftwareDock.Domain.Tests;

public class SecurityAdvisorTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static InstalledApplication App(string name, string? publisher, string? available = "2.0") => new()
    {
        Key = ApplicationKey.Parse(name),
        Name = name,
        Publisher = publisher,
        Origin = ApplicationOrigin.Registry,
        AvailableVersion = available,
    };

    private static KnownExploitedVulnerability Kev(string vendor, string product, int daysAgo, string cve = "CVE-2026-0001") =>
        new(cve, vendor, product, $"{vendor} {product} flaw", Today.AddDays(-daysAgo), false);

    [Fact]
    public void ExploitedProduct_WithPendingUpdate_IsFlagged()
    {
        var firefox = App("Mozilla Firefox (x64 en-US)", "Mozilla");

        var result = SecurityAdvisor.Assess([firefox], [Kev("Mozilla", "Firefox", 10)], Today);

        Assert.Equal("CVE-2026-0001 is being exploited in the wild", result[firefox.Key].Summary);
    }

    [Fact]
    public void UpToDateApps_AreNeverFlagged()
    {
        var firefox = App("Mozilla Firefox (x64 en-US)", "Mozilla", available: null);

        Assert.Empty(SecurityAdvisor.Assess([firefox], [Kev("Mozilla", "Firefox", 10)], Today));
    }

    [Fact]
    public void OldCatalogEntries_AreIgnored()
    {
        var firefox = App("Mozilla Firefox", "Mozilla");

        Assert.Empty(SecurityAdvisor.Assess([firefox], [Kev("Mozilla", "Firefox", 400)], Today));
    }

    [Fact]
    public void ChromiumEntries_ApplyToChrome()
    {
        var chrome = App("Google Chrome", "Google LLC");

        Assert.Single(SecurityAdvisor.Assess([chrome], [Kev("Google", "Chromium V8", 3)], Today));
    }

    [Theory]
    [InlineData("Windows Terminal", "Microsoft Corporation", "Microsoft", "Windows")]
    [InlineData("Microsoft Visual Studio Code", "Microsoft Corporation", "Microsoft", "Office")]
    [InlineData("Git", "The Git Development Community", "Microsoft", "Git")]
    public void LookalikeNames_AreNotMatched(string name, string publisher, string vendor, string product)
    {
        Assert.Empty(SecurityAdvisor.Assess([App(name, publisher)], [Kev(vendor, product, 5)], Today));
    }

    [Theory]
    [InlineData("7-Zip 24.08 (x64)", "7-zip")]
    [InlineData("Mozilla Firefox (x64 en-US)", "mozilla firefox")]
    [InlineData("Notepad++ (64-bit x64)", "notepad++")]
    [InlineData("Microsoft Visual Studio Code 2026", "microsoft visual studio code")]
    public void CoreName_StripsVersionsArchitectureAndLocale(string display, string expected) =>
        Assert.Equal(expected, SecurityAdvisor.CoreName(display));

    [Fact]
    public void ProductNamedAfterItsVendor_Matches()
    {
        var winrar = App("WinRAR 7.01 (64-bit)", "win.rar GmbH");

        Assert.Single(SecurityAdvisor.Assess([winrar], [Kev("RARLAB", "WinRAR", 30)], Today));
    }
}
