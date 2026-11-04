using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SoftwareDock.App.Platform;
using SoftwareDock.Application;
using SoftwareDock.Application.Operations;
using SoftwareDock.Infrastructure;

namespace SoftwareDock.App.Services;

/// <summary>
/// The elevated side of <see cref="UacBatchElevator"/>: no window, no tray, one batch, then exit.
/// Started as <c>SoftwareDock.exe --elevated-worker &lt;pipe&gt; &lt;dock pid&gt;</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ElevatedWorker
{
    public static int Run(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var dockId))
        {
            return 2;
        }

        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(TimeSpan.FromSeconds(30));
        if (!IsTheDock(pipe, dockId))
        {
            return 3;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSoftwareDockApplication();
        services.AddSoftwareDockWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();

        using var reader = new StreamReader(pipe, Encoding.UTF8);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
        BatchProtocol.ServeAsync(reader, writer, provider.GetRequiredService<ISoftwareOperations>(), CancellationToken.None).GetAwaiter().GetResult();
        return 0;
    }

    /// <summary>The pipe's server is the process that started us, and it runs this same executable.</summary>
    private static bool IsTheDock(NamedPipeClientStream pipe, int dockId)
    {
        if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverId) || serverId != dockId)
        {
            return false;
        }

        try
        {
            using var dock = Process.GetProcessById(dockId);
            return string.Equals(dock.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
