using System.Text.Json;
using System.Text.Json.Nodes;
using SoftwareDock.Domain.Backup;
using SoftwareDock.Domain.Packages;

namespace SoftwareDock.Application.Backup;

/// <summary>Reads and writes backups. Reads both formats; tells them apart by shape, not by file name.</summary>
public static class SoftwareManifestJson
{
    public const string FormatName = "software-dock-backup";
    public const int FormatVersion = 1;
    private const string WinGetSchema = "https://aka.ms/winget-packages.schema.2.0.json";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Source details exactly as `winget export` writes them; `winget import` matches sources on these.
    private static readonly Dictionary<string, JsonObject> KnownSources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winget"] = new JsonObject
        {
            ["Argument"] = "https://cdn.winget.microsoft.com/cache",
            ["Identifier"] = "Microsoft.Winget.Source_8wekyb3d8bbwe",
            ["Name"] = "winget",
            ["Type"] = "Microsoft.PreIndexed.Package",
        },
        ["msstore"] = new JsonObject
        {
            ["Argument"] = "https://storeedgefd.dsx.mp.microsoft.com/v9.0",
            ["Identifier"] = "StoreEdgeFD",
            ["Name"] = "msstore",
            ["Type"] = "Microsoft.Rest",
        },
    };

    public static string Write(SoftwareManifest manifest, BackupFormat format)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var root = format == BackupFormat.WinGet ? ToWinGet(manifest) : ToSoftwareDock(manifest);
        return root.ToJsonString(Indented);
    }

    /// <exception cref="FormatException">The text is neither a Software Dock backup nor a WinGet export.</exception>
    public static SoftwareManifest Read(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The file is not valid JSON.", ex);
        }

        return root switch
        {
            JsonObject o when (string?)o["format"] == FormatName => FromSoftwareDock(o),
            JsonObject o when o["Sources"] is JsonArray => FromWinGet(o),
            _ => throw new FormatException("This is not a Software Dock backup or a 'winget export' file."),
        };
    }

    private static JsonObject ToSoftwareDock(SoftwareManifest manifest) => new()
    {
        ["format"] = FormatName,
        ["version"] = FormatVersion,
        ["createdAt"] = manifest.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ["machine"] = manifest.MachineName,
        ["packages"] = new JsonArray(manifest.Packages.Select(p => (JsonNode)new JsonObject
        {
            ["id"] = p.Id.Value,
            ["name"] = p.Name,
            ["source"] = p.Source,
            ["version"] = p.Version,
        }).ToArray()),
        ["installManually"] = new JsonArray(manifest.Unmanaged.Select(u => (JsonNode)new JsonObject
        {
            ["name"] = u.Name,
            ["publisher"] = u.Publisher,
            ["version"] = u.Version,
        }).ToArray()),
    };

    private static JsonObject ToWinGet(SoftwareManifest manifest)
    {
        var sources = manifest.Packages
            .Where(p => p.Source is not null && KnownSources.ContainsKey(p.Source))
            .GroupBy(p => p.Source!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (JsonNode)new JsonObject
            {
                ["Packages"] = new JsonArray(g.Select(p => (JsonNode)new JsonObject { ["PackageIdentifier"] = p.Id.Value }).ToArray()),
                ["SourceDetails"] = KnownSources[g.Key].DeepClone(),
            })
            .ToArray();

        return new JsonObject
        {
            ["$schema"] = WinGetSchema,
            ["CreationDate"] = manifest.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["Sources"] = new JsonArray(sources),
            ["WinGetVersion"] = "1.9.0",
        };
    }

    private static SoftwareManifest FromSoftwareDock(JsonObject root)
    {
        var packages = (root["packages"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(p => (Id: (string?)p["id"], Node: p))
            .Where(p => PackageId.TryParse(p.Id, out _, out _))
            .Select(p => new ManifestPackage(PackageId.Parse(p.Id!), (string?)p.Node["name"] ?? p.Id!, (string?)p.Node["source"], (string?)p.Node["version"]))
            .ToList();
        var manual = (root["installManually"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(u => !string.IsNullOrWhiteSpace((string?)u["name"]))
            .Select(u => new ManifestApplication((string)u["name"]!, (string?)u["publisher"], (string?)u["version"]))
            .ToList();

        return new SoftwareManifest(ParseDate((string?)root["createdAt"]), (string?)root["machine"], packages, manual);
    }

    private static SoftwareManifest FromWinGet(JsonObject root)
    {
        var packages = new List<ManifestPackage>();
        foreach (var source in ((JsonArray)root["Sources"]!).OfType<JsonObject>())
        {
            var sourceName = (string?)source["SourceDetails"]?["Name"];
            foreach (var package in (source["Packages"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (PackageId.TryParse((string?)package["PackageIdentifier"], out var id, out _))
                {
                    packages.Add(new ManifestPackage(id, id.Value, sourceName, (string?)package["Version"]));
                }
            }
        }

        return new SoftwareManifest(ParseDate((string?)root["CreationDate"]), null, packages, []);
    }

    private static DateTimeOffset ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : DateTimeOffset.MinValue;
}
