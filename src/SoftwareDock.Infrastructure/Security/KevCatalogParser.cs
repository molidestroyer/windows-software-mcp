using System.Globalization;
using System.Text.Json;
using SoftwareDock.Domain.Security;

namespace SoftwareDock.Infrastructure.Security;

/// <summary>Reads CISA's <c>known_exploited_vulnerabilities.json</c>.</summary>
internal static class KevCatalogParser
{
    /// <exception cref="FormatException">Not the KEV catalog.</exception>
    public static IReadOnlyList<KnownExploitedVulnerability> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("vulnerabilities", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("The vulnerability catalog has no 'vulnerabilities' array.");
            }

            var result = new List<KnownExploitedVulnerability>(entries.GetArrayLength());
            foreach (var entry in entries.EnumerateArray())
            {
                var cve = Text(entry, "cveID");
                var vendor = Text(entry, "vendorProject");
                var product = Text(entry, "product");
                if (cve is null || vendor is null || product is null
                    || !DateOnly.TryParseExact(Text(entry, "dateAdded"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var added))
                {
                    continue;
                }

                result.Add(new KnownExploitedVulnerability(
                    cve,
                    vendor,
                    product,
                    Text(entry, "vulnerabilityName") ?? cve,
                    added,
                    string.Equals(Text(entry, "knownRansomwareCampaignUse"), "Known", StringComparison.OrdinalIgnoreCase)));
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new FormatException("The vulnerability catalog is not valid JSON.", ex);
        }
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
