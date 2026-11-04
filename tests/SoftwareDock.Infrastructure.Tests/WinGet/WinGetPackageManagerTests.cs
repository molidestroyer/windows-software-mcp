using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Operations;
using SoftwareDock.Domain.Packages;
using SoftwareDock.Infrastructure.Processes;
using SoftwareDock.Infrastructure.WinGet;

namespace SoftwareDock.Infrastructure.Tests.WinGet;

public class WinGetPackageManagerTests
{
    private readonly FakeProcessRunner _runner = new();

    private WinGetPackageManager CreateSut() =>
        new(_runner, Options.Create(new WinGetOptions()), NullLogger<WinGetPackageManager>.Instance);

    [Fact]
    public async Task ListInstalled_WithAvailableColumn_MapsAvailableAndSource()
    {
        _runner.Output = WinGetOutput.Table(
            ["Name", "Id", "Version", "Available", "Source"],
            ["Git", "Git.Git", "2.45.0", "2.47.1", "winget"],
            ["Local thing", @"ARP\User\X64\Thing", "1.0", "", ""]);

        var packages = await CreateSut().ListInstalledAsync(CancellationToken.None);

        Assert.Equal(new InstalledPackage("Git", PackageId.Parse("Git.Git"), "2.45.0", "2.47.1", "winget"), packages[0]);
        Assert.Equal(new InstalledPackage("Local thing", PackageId.Parse(@"ARP\User\X64\Thing"), "1.0", null, null), packages[1]);
    }

    [Fact]
    public async Task ListInstalled_WithoutAvailableColumn_FourthColumnIsTheSource()
    {
        _runner.Output = WinGetOutput.Table(["Name", "Id", "Version", "Source"], ["Git", "Git.Git", "2.47.1", "winget"]);

        var package = Assert.Single(await CreateSut().ListInstalledAsync(CancellationToken.None));

        Assert.Null(package.AvailableVersion);
        Assert.Equal("winget", package.Source);
    }

    [Fact]
    public async Task Search_PassesTheQueryAsANamedOption()
    {
        _runner.Output = WinGetOutput.Table(
            ["Name", "Id", "Version", "Match", "Source"],
            ["VLC media player", "VideoLAN.VLC", "3.0.21", "Tag: video", "winget"]);

        var result = Assert.Single(await CreateSut().SearchAsync("--version", 5, CancellationToken.None));

        Assert.Equal(["search", "--query", "--version", "--count", "5"], _runner.LastRequest!.Arguments.Take(5));
        Assert.Equal(new PackageSearchResult("VLC media player", PackageId.Parse("VideoLAN.VLC"), "3.0.21", "winget"), result);
    }

    [Fact]
    public async Task Install_BuildsAnExactSilentNonInteractiveCommand()
    {
        await CreateSut().InstallAsync(new InstallRequest(PackageId.Parse("Git.Git"), "2.47.1", Scope: InstallScope.User), null, CancellationToken.None);

        var args = _runner.LastRequest!.Arguments;
        Assert.Equal(["install", "--id", "Git.Git", "--exact", "--silent"], args.Take(5));
        Assert.Contains("--disable-interactivity", args);
        Assert.Contains("--accept-package-agreements", args);
        Assert.Equal("2.47.1", args[args.ToList().IndexOf("--version") + 1]);
        Assert.Equal("user", args[args.ToList().IndexOf("--scope") + 1]);
    }

    [Fact]
    public async Task Install_VersionThatLooksLikeASwitch_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateSut().InstallAsync(new InstallRequest(PackageId.Parse("Git.Git"), "--override"), null, CancellationToken.None));
        Assert.Null(_runner.LastRequest);
    }

    [Theory]
    [InlineData(true, "--silent")]
    [InlineData(false, "--interactive")]
    public async Task Uninstall_MapsTheMode(bool silent, string expectedSwitch)
    {
        await CreateSut().UninstallAsync(PackageId.Parse("Git.Git"), silent, null, CancellationToken.None);

        Assert.Contains(expectedSwitch, _runner.LastRequest!.Arguments);
    }

    [Theory]
    [InlineData(0, OperationStatus.Succeeded)]
    [InlineData(3010, OperationStatus.SucceededRebootRequired)]
    [InlineData(1602, OperationStatus.Cancelled)]
    [InlineData(unchecked((int)0x8A150014), OperationStatus.Failed)]
    [InlineData(1603, OperationStatus.Failed)]
    public async Task Upgrade_TranslatesExitCodes(int exitCode, OperationStatus expected)
    {
        _runner.ExitCode = exitCode;
        _runner.Output = "Installer failed with exit code: 1603\n";

        var result = await CreateSut().UpgradeAsync(PackageId.Parse("Git.Git"), null, CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(exitCode, result.ExitCode);
    }

    [Fact]
    public async Task Failure_MessageCarriesWinGetsLastLine()
    {
        _runner.ExitCode = unchecked((int)0x8A150006);
        _runner.Output = "Found Git [Git.Git] Version 2.47.1\nInstaller hash does not match.\n";

        var result = await CreateSut().UpgradeAsync(PackageId.Parse("Git.Git"), null, CancellationToken.None);

        Assert.EndsWith("Installer hash does not match.", result.Message);
    }

    [Fact]
    public async Task MissingWinGet_IsReportedAsUnavailable()
    {
        _runner.Throw = new Win32Exception(2);

        await Assert.ThrowsAsync<PackageManagerUnavailableException>(() => CreateSut().ListInstalledAsync(CancellationToken.None));
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public string Output { get; set; } = string.Empty;

        public int ExitCode { get; set; }

        public Exception? Throw { get; set; }

        public ProcessRequest? LastRequest { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<string>? outputLines, CancellationToken cancellationToken)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            LastRequest = request;
            return Task.FromResult(new ProcessResult(ExitCode, Output, string.Empty));
        }
    }
}
