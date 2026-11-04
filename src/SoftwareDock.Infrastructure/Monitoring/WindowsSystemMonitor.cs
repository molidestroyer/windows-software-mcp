using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Monitoring;

namespace SoftwareDock.Infrastructure.Monitoring;

/// <summary>
/// Processes and memory through Win32.
/// </summary>
/// <remarks>
/// <see cref="Process.MainModule"/> and <see cref="Process.TotalProcessorTime"/> need
/// <c>PROCESS_QUERY_INFORMATION</c> and throw for elevated or protected processes, which a
/// non-elevated tray app sees constantly. <c>PROCESS_QUERY_LIMITED_INFORMATION</c> is granted for
/// almost everything and is enough for the image path and CPU times. Working set comes from the
/// system-wide snapshot <see cref="Process.GetProcesses()"/> already took, with no handle at all.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsSystemMonitor : ISystemMonitor
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public int ProcessorCount => Environment.ProcessorCount;

    public IReadOnlyList<ProcessSample> GetProcesses()
    {
        var processes = Process.GetProcesses();
        var samples = new List<ProcessSample>(processes.Length);
        foreach (var process in processes)
        {
            using (process)
            {
                if (process.Id is 0 or 4)
                {
                    continue; // Idle and System.
                }

                var (path, cpu) = Query(process.Id);
                samples.Add(new ProcessSample(process.Id, path, cpu, SafeWorkingSet(process)));
            }
        }

        return samples;
    }

    public MemoryStatus GetMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryStatus((long)status.TotalPhysical, (long)status.AvailablePhysical)
            : new MemoryStatus(0, 0);
    }

    private static (string? Path, TimeSpan Cpu) Query(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == 0)
        {
            return (null, TimeSpan.Zero);
        }

        try
        {
            var cpu = GetProcessTimes(handle, out _, out _, out var kernel, out var user)
                ? TimeSpan.FromTicks(kernel + user)
                : TimeSpan.Zero;

            const int Capacity = 1024;
            var buffer = stackalloc char[Capacity];
            var length = (uint)Capacity;
            var path = QueryFullProcessImageName(handle, 0, buffer, ref length)
                ? new string(buffer, 0, (int)length)
                : null;
            return (path, cpu);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static long SafeWorkingSet(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch (InvalidOperationException)
        {
            return 0; // Exited between the snapshot and now.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, uint flags, char* exeName, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
