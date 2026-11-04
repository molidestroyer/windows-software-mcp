using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SoftwareDock.Infrastructure.Security;

namespace SoftwareDock.Infrastructure.Tests;

public sealed class KevFeedTests : IDisposable
{
    private const string Catalog = """
        {
          "title": "CISA Catalog of Known Exploited Vulnerabilities",
          "catalogVersion": "2026.11.03",
          "count": 3,
          "vulnerabilities": [
            { "cveID": "CVE-2026-1111", "vendorProject": "Mozilla", "product": "Firefox", "vulnerabilityName": "Mozilla Firefox Use-After-Free", "dateAdded": "2026-10-20", "knownRansomwareCampaignUse": "Known" },
            { "cveID": "CVE-2026-2222", "vendorProject": "Google", "product": "Chromium V8", "vulnerabilityName": "Type Confusion", "dateAdded": "2026-10-01", "knownRansomwareCampaignUse": "Unknown" },
            { "cveID": "CVE-broken", "vendorProject": "X", "product": "Y", "dateAdded": "not a date" }
          ]
        }
        """;

    private readonly string _cache = Path.Combine(Path.GetTempPath(), $"kev-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    [Fact]
    public void Parser_ReadsEntriesAndSkipsBrokenOnes()
    {
        var entries = KevCatalogParser.Parse(Catalog);

        Assert.Equal(["CVE-2026-1111", "CVE-2026-2222"], entries.Select(e => e.CveId));
        Assert.True(entries[0].UsedByRansomware);
        Assert.Equal(new DateOnly(2026, 10, 1), entries[1].DateAdded);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("nope")]
    public void Parser_RejectsAnythingElse(string json) => Assert.Throws<FormatException>(() => KevCatalogParser.Parse(json));

    [Fact]
    public async Task Feed_DownloadsOnce_ThenServesTheCache()
    {
        var handler = new CountingHandler(Catalog);
        var feed = Create(handler);

        await feed.GetAsync(CancellationToken.None);
        await feed.GetAsync(CancellationToken.None);

        Assert.Equal(1, handler.Requests);
        Assert.True(File.Exists(Path.Combine(_cache, "known_exploited_vulnerabilities.json")));
    }

    [Fact]
    public async Task Feed_Offline_FallsBackToTheStaleCache()
    {
        await Create(new CountingHandler(Catalog)).GetAsync(CancellationToken.None);
        File.SetLastWriteTimeUtc(Path.Combine(_cache, "known_exploited_vulnerabilities.json"), DateTime.UtcNow.AddDays(-3));

        var entries = await Create(new CountingHandler(null)).GetAsync(CancellationToken.None);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task Feed_OfflineWithoutCache_Throws()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Create(new CountingHandler(null)).GetAsync(CancellationToken.None));
    }

    private CisaKevFeed Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new SecurityFeedOptions { CacheDirectory = _cache }), _time, NullLogger<CisaKevFeed>.Instance);

    private sealed class CountingHandler(string? body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return body is null
                ? throw new HttpRequestException("offline")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
