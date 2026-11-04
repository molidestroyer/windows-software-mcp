using Microsoft.Extensions.Logging;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Application.Inventory;
using SoftwareDock.Domain.Applications;
using SoftwareDock.Domain.Security;
using SoftwareDock.Domain.Usage;

namespace SoftwareDock.Application.Insights;

/// <summary>
/// Facts about installed software that need more than the inventory: which apps have actively
/// exploited vulnerabilities and an update pending, and when each was last used. Both sources are
/// optional — a failure yields "no insight", never an error in the user's face.
/// </summary>
public sealed partial class SoftwareInsights(
    IVulnerabilityFeed feed,
    IUsageHistorySource usage,
    IStartupItemSource startup,
    ISystemMonitor system,
    TimeProvider time,
    ILogger<SoftwareInsights> logger)
{
    public async Task<IReadOnlyDictionary<ApplicationKey, SecurityAdvisory>> GetSecurityAdvisoriesAsync(
        InventorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            var catalog = await feed.GetAsync(cancellationToken);
            return SecurityAdvisor.Assess(snapshot.Applications, catalog, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogFeedFailed(ex);
            return new Dictionary<ApplicationKey, SecurityAdvisory>();
        }
    }

    public IReadOnlyDictionary<ApplicationKey, ApplicationUsage> GetUsage(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            return UsageAttribution.Attribute(snapshot.Applications, usage.GetUsage());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LogUsageFailed(ex);
            return new Dictionary<ApplicationKey, ApplicationUsage>();
        }
    }

    /// <summary>
    /// Applications not opened for <paramref name="threshold"/> that are not running, not enabled at
    /// startup and not part of Windows (<see cref="UnusedApplications"/>).
    /// </summary>
    public async Task<IReadOnlyDictionary<ApplicationKey, ApplicationUsage>> GetUnusedAsync(
        InventorySnapshot snapshot,
        TimeSpan? threshold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var lastUsed = GetUsage(snapshot);
        var ownerOf = ApplicationRoots.Lookup(snapshot.Applications);
        var inUse = new HashSet<ApplicationKey>();

        try
        {
            foreach (var item in await startup.ListAsync(cancellationToken))
            {
                if (item.IsEnabled && ownerOf(item.ExecutablePath) is { } key)
                {
                    inUse.Add(key);
                }
            }

            foreach (var process in await Task.Run(system.GetProcesses, cancellationToken))
            {
                if (ownerOf(process.ImagePath) is { } key)
                {
                    inUse.Add(key);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            // Without knowing what is in use, saying "unused" could be wrong: say nothing.
            LogUsageFailed(ex);
            return new Dictionary<ApplicationKey, ApplicationUsage>();
        }

        return UnusedApplications.Find(snapshot.Applications, lastUsed, time.GetUtcNow(), inUse, threshold);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The vulnerability catalog is unavailable; no security advisories this time")]
    private partial void LogFeedFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Usage history could not be read")]
    private partial void LogUsageFailed(Exception exception);
}
