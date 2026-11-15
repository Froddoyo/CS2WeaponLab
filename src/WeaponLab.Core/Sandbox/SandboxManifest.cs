using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeaponLab.Core.Sandbox;

/// <summary>
/// The contents of <c>sandbox.json</c>: which weapon the sandbox is for,
/// the game build it was imported from, and a fingerprint of every
/// template clip. Edits are stored in separate files in later phases.
/// </summary>
public sealed record SandboxManifest
{
    public const string CurrentSchema = "weaponlab.sandbox/1";
    public const string FileName = "sandbox.json";

    public string Schema { get; init; } = CurrentSchema;
    public required string WeaponId { get; init; }
    public required string Category { get; init; }
    public required string VpkFolder { get; init; }
    public string? TargetAddon { get; init; }

    /// <summary>The addon whose own clips the templates were taken from; null when they are the game's.</summary>
    public string? SourceAddon { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required SandboxGameBuild Game { get; init; }
    public required IReadOnlyList<SandboxClip> Clips { get; init; }

    /// <summary>
    /// The weapon's third-person clips (0.26), kept apart from <see cref="Clips"/> so everything first-person
    /// (events, game timing, custom skeletons, the viewport) stays as it was. Empty in sandboxes made before 0.26
    /// until "Add third-person clips" (<see cref="SandboxStore.AddWorldClips"/>).
    /// </summary>
    public IReadOnlyList<SandboxClip> WorldClips { get; init; } = [];

    /// <summary>First-person then third-person clips.</summary>
    public IEnumerable<SandboxClip> AllClips() => Clips.Concat(WorldClips);

    /// <summary>The clip with this id (<see cref="Blender.RigPackage.ClipId"/>), first or third person; null when there is none.</summary>
    public SandboxClip? FindClip(string clipId) => AllClips().FirstOrDefault(clip => Blender.RigPackage.ClipId(clip.ResourcePath) == clipId);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions) + "\n";

    public static SandboxManifest FromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<SandboxManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("sandbox.json is empty.");
        if (manifest.Schema != CurrentSchema)
        {
            throw new InvalidDataException($"Unsupported sandbox schema '{manifest.Schema}' (expected {CurrentSchema}).");
        }

        return manifest;
    }
}

public sealed record SandboxGameBuild(string PatchVersion, string ClientVersion, string SourceRevision);

/// <param name="Template">Path of the untouched copy, relative to the sandbox folder.</param>
/// <param name="AddonClip">The addon clip the template was copied from (without <c>_c</c>), when the sandbox starts from the addon.</param>
public sealed record SandboxClip(
    string ResourcePath,
    string Template,
    string Sha256,
    int FrameCount,
    double DurationSeconds,
    bool Editable,
    string? AddonClip = null);
