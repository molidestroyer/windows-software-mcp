using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoftwareDock.Application.Abstractions;
using SoftwareDock.Domain.Security;

namespace SoftwareDock.Infrastructure.Security;

public sealed class SecurityFeedOptions
{
    public const string SectionName = "SecurityFeed";

    public Uri CatalogUrl { get; set; } = new("https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json");

    /// <summary>The catalog changes a few times a week; once a day is plenty.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(24);

    public string CacheDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftwareDock", "cache");
}

/// <summary>
/// CISA's catalog, cached on disk. When CISA is unreachable a stale copy is better than nothing:
/// the entries only ever grow.
/// </summary>
public sealed partial class CisaKevFeed(HttpClient http, IOptions<SecurityFeedOptions> options, TimeProvider time, ILogger<CisaKevFeed> logger)
    : IVulnerabilityFeed
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset LoadedAt, IReadOnlyList<KnownExploitedVulnerability> Catalog)? _memory;

    public async Task<IReadOnlyList<KnownExploitedVulnerability>> GetAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_memory is { } cached && time.GetUtcNow() - cached.LoadedAt < settings.RefreshInterval)
            {
                return cached.Catalog;
            }

            var file = Path.Combine(settings.CacheDirectory, "known_exploited_vulnerabilities.json");
            if (File.Exists(file) && time.GetUtcNow() - File.GetLastWriteTimeUtc(file) < settings.RefreshInterval)
            {
                return Remember(KevCatalogParser.Parse(await File.ReadAllTextAsync(file, cancellationToken)));
            }

            try
            {
                var json = await http.GetStringAsync(settings.CatalogUrl, cancellationToken);
                var catalog = KevCatalogParser.Parse(json);
                Directory.CreateDirectory(settings.CacheDirectory);
                await File.WriteAllTextAsync(file, json, cancellationToken);
                LogRefreshed(catalog.Count);
                return Remember(catalog);
            }
            catch (HttpRequestException ex) when (File.Exists(file))
            {
                LogUsingStaleCache(ex);
                return Remember(KevCatalogParser.Parse(await File.ReadAllTextAsync(file, cancellationToken)));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<KnownExploitedVulnerability> Remember(IReadOnlyList<KnownExploitedVulnerability> catalog)
    {
        _memory = (time.GetUtcNow(), catalog);
        return catalog;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Known exploited vulnerabilities catalog refreshed: {Count} entries")]
    private partial void LogRefreshed(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CISA is unreachable; using the cached vulnerability catalog")]
    private partial void LogUsingStaleCache(Exception exception);
}
