using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponLab.Core.IO;

namespace WeaponLab.Core.Delivery;

/// <summary>
/// What Weapon Lab put into an addon (<c>weaponlab_delivery.json</c> in the
/// addon's game folder): every file with its hash, the delivered clips per
/// weapon, and the vdata values it set. Repair and status checks compare the
/// addon against this.
/// </summary>
public sealed class AddonManifest
{
    public const string FileName = "weaponlab_delivery.json";
    public const string CurrentSchema = "weaponlab.delivery/1";
    public const string ContentPrefix = "content:";
    public const string CompiledVDataPath = "scripts/weapons.vdata_c";
    public const string VDataSourceKey = ContentPrefix + VDataCompiler.ContentRelativePath;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Schema { get; set; } = CurrentSchema;
    public string? Profile { get; set; }

    /// <summary>The Weapon Lab workspace that made this delivery (full path); null for deliveries before 0.12.</summary>
    public string? Workspace { get; set; }
    public DateTimeOffset DeliveredUtc { get; set; }

    /// <summary>Path relative to the addon game folder (or "content:" + path relative to its content folder) → SHA-256.</summary>
    public SortedDictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>File key → the profile that last pushed it; a push from another profile over it is flagged first.</summary>
    public SortedDictionary<string, string> FileProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Weapon folder → delivered clip ids.</summary>
    public SortedDictionary<string, List<string>> Clips { get; set; } = new(StringComparer.Ordinal);

    /// <summary>vdata values Weapon Lab set: block → path → value.</summary>
    public SortedDictionary<string, SortedDictionary<string, string>> VData { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Blocks Weapon Lab based on another weapon: block → the top-level keys it replaced.</summary>
    public SortedDictionary<string, List<string>> VDataClasses { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Graph edits delivered: stock graph path → edit key → value.</summary>
    public SortedDictionary<string, SortedDictionary<string, string>> Graphs { get; set; } = new(StringComparer.Ordinal);

    public string? PlayerModelGraph { get; set; }
    public bool GameTested { get; set; }

    public static AddonManifest? Read(AddonTarget target)
    {
        if (!File.Exists(target.ManifestPath))
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize<AddonManifest>(File.ReadAllText(target.ManifestPath), Json)
            ?? throw new InvalidDataException("The addon's delivery manifest is empty.");
        if (manifest.Schema != CurrentSchema)
        {
            throw new InvalidDataException($"{target.ManifestPath} is not a Weapon Lab delivery manifest.");
        }

        return manifest;
    }

    public byte[] ToBytes() => System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json) + "\n");

    public void Write(AddonTarget target) => AtomicFile.WriteAllBytes(target.ManifestPath, ToBytes());

    /// <summary>Absolute path of a manifest file entry.</summary>
    public static string Resolve(AddonTarget target, string entry)
    {
        return entry.StartsWith(ContentPrefix, StringComparison.Ordinal)
            ? AtomicFile.SafeJoin(target.ContentPath, entry[ContentPrefix.Length..])
            : AtomicFile.SafeJoin(target.GamePath, entry);
    }

    /// <summary>The manifest key for an absolute path inside the addon's game or content folder.</summary>
    public static string KeyFor(AddonTarget target, string path)
    {
        var full = Path.GetFullPath(path);
        var content = Path.GetFullPath(target.ContentPath) + Path.DirectorySeparatorChar;
        return full.StartsWith(content, StringComparison.OrdinalIgnoreCase)
            ? ContentPrefix + full[content.Length..].Replace(Path.DirectorySeparatorChar, '/')
            : Path.GetRelativePath(target.GamePath, full).Replace(Path.DirectorySeparatorChar, '/');
    }

    public static bool IsVData(string entry) =>
        entry.Equals(CompiledVDataPath, StringComparison.OrdinalIgnoreCase) || entry.Equals(VDataSourceKey, StringComparison.OrdinalIgnoreCase);
}
