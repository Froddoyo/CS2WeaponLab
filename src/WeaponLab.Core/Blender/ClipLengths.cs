using System.Text.Json;
using WeaponLab.Core.Export;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Blender;

/// <summary>
/// Tells the Blender add-on which clips the user gave another length (stored
/// with their event edits, see <see cref="Clips.ClipLength"/>), so Blender shows
/// and samples that many frames: <c>blender/input/clip_lengths.json</c>.
/// </summary>
public static class ClipLengths
{
    public const string FileName = "clip_lengths.json";
    public const string Schema = "weaponlab.cliplengths/1";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Writes the file (removed when no clip has another length). Called with every event write, build and open.</summary>
    public static void Write(SandboxInfo sandbox)
    {
        var input = RigPackage.InputPath(sandbox);
        var path = Path.Combine(input, FileName);
        var lengths = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var clip in sandbox.Manifest.Clips)
        {
            var clipId = RigPackage.ClipId(clip.ResourcePath);
            if (EventEdits.Read(sandbox, clipId).Frames is { } frames)
            {
                lengths[clipId] = frames;
            }
        }

        // No .blend built yet means no input folder: nothing to tell Blender (File.Delete throws on a missing folder).
        if (lengths.Count == 0 || !Directory.Exists(input))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new { schema = Schema, clips = lengths }, Json) + "\n");
    }
}
