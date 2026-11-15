using System.Text.Json;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;

namespace WeaponLab.Core.Delivery;

/// <summary>
/// How an addon had each file before Weapon Lab first changed it, so a
/// delivery builds on the addon's own work and undoing an edit puts the
/// addon's version back instead of CS2's. Kept in
/// <c>workspace/addons/&lt;addon&gt;/originals</c>, keyed like the manifest
/// (game-relative path, or "content:" + path).
/// </summary>
public sealed class AddonOriginals
{
    private const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _folder;
    private readonly SortedDictionary<string, Entry> _entries;

    private AddonOriginals(string folder, SortedDictionary<string, Entry> entries)
    {
        _folder = folder;
        _entries = entries;
    }

    /// <param name="Existed">False when the addon had no such file: removing ours leaves nothing.</param>
    public sealed record Entry(bool Existed, string? Sha256);

    public static string AddonFolder(string workspacePath, string addonName) => Path.Combine(workspacePath, "addons", addonName);

    public static AddonOriginals Load(string workspacePath, string addonName)
    {
        var folder = Path.Combine(AddonFolder(workspacePath, addonName), "originals");
        var index = Path.Combine(folder, IndexFileName);
        var entries = File.Exists(index)
            ? JsonSerializer.Deserialize<SortedDictionary<string, Entry>>(File.ReadAllText(index), Json) ?? []
            : [];
        return new AddonOriginals(folder, new SortedDictionary<string, Entry>(entries, StringComparer.OrdinalIgnoreCase));
    }

    public bool IsRecorded(string key) => _entries.ContainsKey(key);

    /// <summary>
    /// The addon's own version of a file: the recorded original, or the file
    /// on disk when Weapon Lab never wrote it. Null when the addon had none,
    /// or when only Weapon Lab ever wrote it (deliveries before 0.10 kept no
    /// originals; their files count as Weapon Lab's).
    /// </summary>
    public byte[]? Before(string key, AddonTarget target, AddonManifest? manifest)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            return entry.Existed ? File.ReadAllBytes(StoredPath(key)) : null;
        }

        if (manifest?.Files.ContainsKey(key) == true)
        {
            return null;
        }

        var path = AddonManifest.Resolve(target, key);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Records a file's state before Weapon Lab first changes it. Does nothing once recorded.</summary>
    public void Record(string key, AddonTarget target, AddonManifest? manifest)
    {
        if (_entries.ContainsKey(key))
        {
            return;
        }

        var path = AddonManifest.Resolve(target, key);
        var owned = manifest?.Files.ContainsKey(key) == true;
        if (owned || !File.Exists(path))
        {
            _entries[key] = new Entry(false, null);
            return;
        }

        var bytes = File.ReadAllBytes(path);
        AtomicFile.WriteAllBytes(StoredPath(key), bytes);
        _entries[key] = new Entry(true, GameArchive.Sha256(bytes));
    }

    /// <summary>Drops a recorded original: from now on the file as it is on disk (or its absence) is the addon's own.</summary>
    public void Forget(string key) => _entries.Remove(key);

    public void Save()
    {
        Directory.CreateDirectory(_folder);
        AtomicFile.WriteAllText(Path.Combine(_folder, IndexFileName), JsonSerializer.Serialize(_entries, Json) + "\n");
    }

    private string StoredPath(string key) =>
        AtomicFile.SafeJoin(_folder, Path.Combine("files", key.Replace(':', '_')));

    /// <summary>
    /// Copies the addon's animation and scripts folders (game and content)
    /// the first time Weapon Lab delivers to it. A safety net beside the
    /// per-file originals; never overwritten.
    /// </summary>
    public static string? SnapshotOnce(string workspacePath, AddonTarget target)
    {
        var snapshot = Path.Combine(AddonFolder(workspacePath, target.Name), "first-snapshot");
        if (Directory.Exists(snapshot))
        {
            return null;
        }

        var staging = snapshot + ".staging";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        foreach (var (root, relative, label) in new[]
                 {
                     (target.GamePath, "animation", "game"),
                     (target.GamePath, "scripts", "game"),
                     (target.ContentPath, "scripts", "content"),
                 })
        {
            var source = Path.Combine(root, relative);
            if (!Directory.Exists(source))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(staging, label, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }

        Directory.CreateDirectory(staging);
        Directory.Move(staging, snapshot);
        return snapshot;
    }
}
