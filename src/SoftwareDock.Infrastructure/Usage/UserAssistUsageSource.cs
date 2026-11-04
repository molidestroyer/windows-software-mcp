using System.Runtime.Versioning;
using Microsoft.Win32;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Usage;

namespace SoftwareDock.Infrastructure.Usage;

[SupportedOSPlatform("windows")]
public sealed class UserAssistUsageSource : IUsageHistorySource
{
    // The "executables" UserAssist store (the other GUID holds shortcuts).
    private const string ExecutablesKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

    private static readonly Dictionary<Guid, Environment.SpecialFolder> KnownFolders = new()
    {
        [new Guid("6D809377-6AF0-444B-8957-A3773F02200E")] = Environment.SpecialFolder.ProgramFiles,
        [new Guid("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E")] = Environment.SpecialFolder.ProgramFilesX86,
        [new Guid("1AC14E77-02E7-4E5D-B744-2EB1AE5198B7")] = Environment.SpecialFolder.System,
        [new Guid("D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27")] = Environment.SpecialFolder.SystemX86,
        [new Guid("F38BF404-1D43-42F2-9305-67DE0B28FC23")] = Environment.SpecialFolder.Windows,
        [new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091")] = Environment.SpecialFolder.LocalApplicationData,
        [new Guid("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D")] = Environment.SpecialFolder.ApplicationData,
    };

    public IReadOnlyList<UsageRecord> GetUsage()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ExecutablesKey);
        if (key is null)
        {
            return [];
        }

        return key.GetValueNames()
            .Select(name => UserAssistDecoder.Decode(name, key.GetValue(name) as byte[], Resolve))
            .OfType<UsageRecord>()
            .ToList();
    }

    private static string? Resolve(Guid id) =>
        KnownFolders.TryGetValue(id, out var folder) ? Environment.GetFolderPath(folder) : null;
}
