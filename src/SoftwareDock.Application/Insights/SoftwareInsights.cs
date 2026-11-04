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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The vulnerability catalog is unavailable; no security advisories this time")]
    private partial void LogFeedFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Usage history could not be read")]
    private partial void LogUsageFailed(Exception exception);
}
