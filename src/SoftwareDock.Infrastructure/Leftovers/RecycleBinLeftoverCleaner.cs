using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Leftovers;
using SoftwareDock.Domain.Operations;

namespace SoftwareDock.Infrastructure.Leftovers;

/// <summary>
/// Finds leftover folders and moves them to the Recycle Bin through the shell. Anything the bin
/// cannot take triggers the shell's own "delete permanently?" prompt rather than a silent delete.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class RecycleBinLeftoverCleaner(ISystemMonitor system) : ILeftoverCleaner
{
    private const uint FoDelete = 0x3;
    private const ushort FofSilent = 0x4;
    private const ushort FofNoConfirmation = 0x10;
    private const ushort FofAllowUndo = 0x40;
    private const ushort FofNoErrorUi = 0x400;

    // Partially overrides FOF_NOCONFIRMATION: asks before destroying anything that cannot be
    // recycled (too large for the bin, or a drive without one) instead of deleting it silently.
    private const ushort FofWantNukeWarning = 0x4000;
    private const int MaxFilesMeasured = 200_000;

    public Task<IReadOnlyList<LeftoverFolder>> FindAsync(
        InstalledApplication application,
        IReadOnlyCollection<InstalledApplication> stillInstalled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var folders = new KnownFolders(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        return Task.Run<IReadOnlyList<LeftoverFolder>>(
            () => LeftoverCandidates.For(application, folders, stillInstalled, FoldersInUse())
                .Where(Directory.Exists)
                .Select(path => new LeftoverFolder(path, Measure(path)))
                .ToList(),
            cancellationToken);
    }

    public Task<OperationResult> RecycleAsync(IReadOnlyList<LeftoverFolder> folders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        // Re-checked at the last moment: something may have started from, or been added to PATH
        // inside, one of these folders since they were offered.
        var inUse = FoldersInUse().Select(f => ApplicationRoots.NormalizePath(f, isDirectory: true)).OfType<string>().Where(f => f.Length > 3).ToList();
        var existing = folders
            .Where(f => Directory.Exists(f.Path))
            .Where(f => ApplicationRoots.NormalizePath(f.Path, isDirectory: true) is { } path
                && !inUse.Any(u => u.StartsWith(path, StringComparison.Ordinal) || path.StartsWith(u, StringComparison.Ordinal)))
            .ToList();
        if (existing.Count == 0)
        {
            return Task.FromResult(OperationResult.Success("Nothing left to clean."));
        }

        return Task.Run(() =>
        {
            // pFrom is a list of paths, each NUL-terminated, ended by an extra NUL.
            var from = Marshal.StringToHGlobalUni(string.Concat(existing.Select(f => f.Path + '\0')) + '\0');
            try
            {
                var operation = new ShFileOperation
                {
                    Function = FoDelete,
                    From = from,
                    Flags = FofAllowUndo | FofNoConfirmation | FofWantNukeWarning | FofSilent | FofNoErrorUi,
                };
                var code = SHFileOperation(ref operation);
                return code == 0 && !operation.AnyOperationsAborted
                    ? OperationResult.Success($"Moved {existing.Count} folder{(existing.Count == 1 ? string.Empty : "s")} to the Recycle Bin.")
                    : OperationResult.Failure($"Some folders could not be moved to the Recycle Bin (error {code}); they may be in use.", code);
            }
            finally
            {
                Marshal.FreeHGlobal(from);
            }
        }, cancellationToken);
    }

    /// <summary>Folders on the process, user and machine PATH, and folders of running processes.</summary>
    private IEnumerable<string> FoldersInUse()
    {
        var path = string.Join(
            ';',
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty,
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty);

        var onPath = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Environment.ExpandEnvironmentVariables);
        var running = system.GetProcesses()
            .Select(p => p.ImagePath)
            .OfType<string>()
            .Select(Path.GetDirectoryName)
            .OfType<string>();
        return onPath.Concat(running).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static long Measure(string path)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return new DirectoryInfo(path).EnumerateFiles("*", options).Take(MaxFilesMeasured).Sum(f => f.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOperation
    {
        public nint Window;
        public uint Function;
        public nint From;
        public nint To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public nint NameMappings;
        public nint ProgressTitle;
    }

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
#pragma warning disable SYSLIB1054 // A struct with a BOOL field needs runtime marshalling; LibraryImport would require DisableRuntimeMarshalling for the whole assembly.
    private static extern int SHFileOperation(ref ShFileOperation operation);
#pragma warning restore SYSLIB1054
}
