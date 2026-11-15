using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using WeaponLab.Core.Clips;

namespace WeaponLab.Core.Export;

/// <summary>
/// Reads the pose files shared with Blender (rig package poses, build
/// references, exported edits): <c>{"arms": {...}, "weapon": {...}}</c>,
/// each a flat list of frames × tracks × [tx, ty, tz, qx, qy, qz, qw, scale].
/// </summary>
public static class PoseFile
{
    public static IReadOnlyDictionary<string, BonePose[][]> Read(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);

        var streams = new Dictionary<string, BonePose[][]>(StringComparer.Ordinal);
        foreach (var stream in document.RootElement.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.Object))
        {
            streams[stream.Name] = ReadStream(stream.Value, $"{Path.GetFileName(path)}:{stream.Name}");
        }

        return streams;
    }

    /// <summary>
    /// Event edits Blender sends with a sample: <c>"events": [{"index", "name", "frame"}]</c>,
    /// one entry per sound marker. Empty when the file has none.
    /// </summary>
    public static IReadOnlyList<(int Index, string? Name, double Frame)> ReadEvents(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        if (!document.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return events.EnumerateArray()
            .Select(entry => (
                entry.GetProperty("index").GetInt32(),
                entry.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null,
                entry.GetProperty("frame").GetDouble()))
            .ToList();
    }

    /// <summary>Writes streams in the same format, keyed by name.</summary>
    public static void Write(string path, IReadOnlyDictionary<string, (string Skeleton, BonePose[][] Poses)> streams) => Write(path, streams, null);

    /// <summary>Writes streams with their bone names and parent names (0.25), so a file can be mapped by name.</summary>
    public static void Write(string path, IReadOnlyDictionary<string, (string Skeleton, BonePose[][] Poses)> streams,
        IReadOnlyDictionary<string, (IReadOnlyList<string> Names, IReadOnlyList<string?> Parents)>? bones)
    {
        var document = streams.ToDictionary(
            pair => pair.Key,
            pair => (object)new
            {
                frames = pair.Value.Poses.Length,
                tracks = pair.Value.Poses.Length == 0 ? 0 : pair.Value.Poses[0].Length,
                skeleton = pair.Value.Skeleton,
                values = pair.Value.Poses.SelectMany(frame => frame).SelectMany(pose => new[]
                {
                    pose.Translation.X, pose.Translation.Y, pose.Translation.Z,
                    pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W, pose.Scale,
                }),
                bones = bones?.GetValueOrDefault(pair.Key).Names,
                parents = bones?.GetValueOrDefault(pair.Key).Parents,
            });

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        JsonSerializer.Serialize(gzip, document, SkipNulls);
    }

    private static readonly JsonSerializerOptions SkipNulls = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static BonePose[][] ReadStream(JsonElement stream, string label)
    {
        var frames = stream.GetProperty("frames").GetInt32();
        var tracks = stream.GetProperty("tracks").GetInt32();
        var values = stream.GetProperty("values");
        if (values.GetArrayLength() != frames * tracks * 8)
        {
            throw new InvalidDataException($"{label}: expected {frames * tracks * 8} values, found {values.GetArrayLength()}.");
        }

        var poses = new BonePose[frames][];
        using var items = values.EnumerateArray();
        for (var frame = 0; frame < frames; frame++)
        {
            poses[frame] = new BonePose[tracks];
            for (var track = 0; track < tracks; track++)
            {
                float Next()
                {
                    if (!items.MoveNext())
                    {
                        throw new InvalidDataException($"{label}: pose data ended early.");
                    }

                    return items.Current.GetSingle();
                }

                var translation = new Vector3(Next(), Next(), Next());
                var rotation = new Quaternion(Next(), Next(), Next(), Next());
                poses[frame][track] = new BonePose(translation, rotation, Next());
            }
        }

        return poses;
    }
}
