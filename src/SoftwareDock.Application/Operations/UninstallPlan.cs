using SoftwareDock.Domain.Applications;

namespace SoftwareDock.Application.Operations;

internal enum UninstallKind
{
    PackageManager,
    RegisteredUninstaller,
}

/// <summary>
/// Decides how an application is removed. Kept apart from the orchestration so the policy can
/// be read — and tested — on its own.
/// </summary>
/// <remarks>
/// Preference order:
/// <list type="number">
///   <item>The package manager, whenever it has a usable id: it knows each installer's silent
///         switches and records the removal.</item>
///   <item>Silent mode: <c>QuietUninstallString</c>, then an MSI product code (<c>/qn</c>).</item>
///   <item>The registered <c>UninstallString</c>, interactively. An unknown EXE uninstaller is
///         never forced silent with guessed switches.</item>
/// </list>
/// </remarks>
internal sealed record UninstallPlan(UninstallKind Kind, bool Silent, UninstallCommand? Command)
{
    public static UninstallPlan? For(InstalledApplication application, UninstallMode mode)
    {
        var silent = mode == UninstallMode.Silent;

        if (application.IsManagedByPackageManager)
        {
            return new UninstallPlan(UninstallKind.PackageManager, silent, null);
        }

        if (silent && application.QuietUninstall is { } quiet)
        {
            // Silent by definition: an EXE carries its own switches, an MSI gets /qn.
            return new UninstallPlan(UninstallKind.RegisteredUninstaller, Silent: true, quiet);
        }

        if (application.Uninstall is { } uninstall)
        {
            return new UninstallPlan(UninstallKind.RegisteredUninstaller, silent && uninstall.IsWindowsInstaller, uninstall);
        }

        return application.QuietUninstall is { } fallback
            ? new UninstallPlan(UninstallKind.RegisteredUninstaller, Silent: true, fallback)
            : null;
    }
}
