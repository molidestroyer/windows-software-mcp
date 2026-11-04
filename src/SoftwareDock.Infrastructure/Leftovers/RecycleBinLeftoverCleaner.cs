using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Leftovers;
using SoftwareDock.Domain.Operations;

namespace SoftwareDock.Infrastructure.Leftovers;

/// <summary>Finds leftover folders and moves them to the Recycle Bin through the shell.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class RecycleBinLeftoverCleaner : ILeftoverCleaner
{
    private const uint FoDelete = 0x3;
    private const ushort FofSilent = 0x4;
    private const ushort FofNoConfirmation = 0x10;
    private const ushort FofAllowUndo = 0x40;
    private const ushort FofNoErrorUi = 0x400;
    private const int MaxFilesMeasured = 200_000;

    public Task<IReadOnlyList<LeftoverFolder>> FindAsync(InstalledApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var folders = new KnownFolders(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        return Task.Run<IReadOnlyList<LeftoverFolder>>(
            () => LeftoverCandidates.For(application, folders)
                .Where(Directory.Exists)
                .Select(path => new LeftoverFolder(path, Measure(path)))
                .ToList(),
            cancellationToken);
    }

    public Task<OperationResult> RecycleAsync(IReadOnlyList<LeftoverFolder> folders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var existing = folders.Where(f => Directory.Exists(f.Path)).ToList();
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
                    Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
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
