using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Domain.Tests;

public class UninstallCommandTests
{
    [Fact]
    public void TryParse_QuotedPath_SplitsExecutableFromArguments()
    {
        Assert.True(UninstallCommand.TryParse(@"""C:\Program Files\App\unins000.exe"" /SILENT", out var command));

        Assert.Equal(@"C:\Program Files\App\unins000.exe", command!.FileName);
        Assert.Equal("/SILENT", command.Arguments);
        Assert.False(command.IsWindowsInstaller);
    }

    [Fact]
    public void TryParse_UnquotedPathWithSpaces_CutsAfterTheExecutable()
    {
        Assert.True(UninstallCommand.TryParse(@"C:\Program Files (x86)\Some App\uninstall.exe /S _?=C:\x", out var command));

        Assert.Equal(@"C:\Program Files (x86)\Some App\uninstall.exe", command!.FileName);
        Assert.Equal(@"/S _?=C:\x", command.Arguments);
    }

    [Fact]
    public void TryParse_ExeInsideAFolderName_IsNotMistakenForTheExecutable()
    {
        Assert.True(UninstallCommand.TryParse(@"C:\Tools\my.exetools\remove.exe --all", out var command));

        Assert.Equal(@"C:\Tools\my.exetools\remove.exe", command!.FileName);
        Assert.Equal("--all", command.Arguments);
    }

    [Theory]
    [InlineData("MsiExec.exe /I{23170F69-40C1-2702-2301-000001000000}")]
    [InlineData("MsiExec.exe /X{23170f69-40c1-2702-2301-000001000000}")]
    [InlineData(@"""C:\Windows\System32\msiexec.exe"" /x {23170F69-40C1-2702-2301-000001000000} /quiet")]
    public void TryParse_WindowsInstaller_ExtractsProductCode(string commandLine)
    {
        Assert.True(UninstallCommand.TryParse(commandLine, out var command));

        Assert.True(command!.IsWindowsInstaller);
        Assert.Equal("{23170F69-40C1-2702-2301-000001000000}", command.MsiProductCode);
    }

    [Fact]
    public void ToProcessCommand_WindowsInstaller_AlwaysUninstallsInsteadOfOpeningRepair()
    {
        UninstallCommand.TryParse("MsiExec.exe /I{23170F69-40C1-2702-2301-000001000000}", out var command);

        Assert.Equal(("msiexec.exe", "/X{23170F69-40C1-2702-2301-000001000000}"), command!.ToProcessCommand(silent: false));
        Assert.Equal(("msiexec.exe", "/X{23170F69-40C1-2702-2301-000001000000} /passive /norestart"), command.ToProcessCommand(silent: true));
    }

    [Fact]
    public void ToProcessCommand_ExeUninstaller_NeverGuessesSilentSwitches()
    {
        UninstallCommand.TryParse(@"""C:\App\uninstall.exe""", out var command);

        Assert.Equal((@"C:\App\uninstall.exe", string.Empty), command!.ToProcessCommand(silent: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Blank_ReturnsFalse(string? commandLine)
    {
        Assert.False(UninstallCommand.TryParse(commandLine, out var command));
        Assert.Null(command);
    }
}
