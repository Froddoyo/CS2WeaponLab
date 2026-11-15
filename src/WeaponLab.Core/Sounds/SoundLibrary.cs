using System.Collections.Concurrent;
using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Sounds;

/// <summary>A soundevent resolved for playback: its sound files (the game picks one at random), volume and pitch.</summary>
/// <param name="DefinedIn">The <c>.vsndevts</c> file that defines it (an addon's own file wins over CS2's).</param>
public sealed record SoundEventInfo(string Name, IReadOnlyList<string> Files, double Volume, double Pitch, string DefinedIn);

/// <summary>A decoded <c>.vsnd</c>: a playable audio file (MP3, or WAV with its header).</summary>
public sealed record DecodedSound(string Path, byte[] Data, bool IsMp3, double Seconds);

/// <summary>
/// CS2's soundevents (every <c>soundevents/*.vsndevts_c</c> in the game and
/// the addon) and their sound files, for picking and auditioning the sound
/// events of a clip. An event may inherit from another through <c>base</c>.
/// </summary>
public sealed class SoundLibrary
{
    private const string Folder = "soundevents/";
    private const int MaxBaseDepth = 8;

    private readonly Dictionary<string, (KVObject Value, string File)> _events;
    private readonly GameArchive _archive;
    private readonly string? _addonGamePath;
    private readonly ConcurrentDictionary<string, DecodedSound?> _decoded = new(StringComparer.OrdinalIgnoreCase);

    private SoundLibrary(Dictionary<string, (KVObject, string)> events, GameArchive archive, string? addonGamePath)
    {
        _events = events;
        _archive = archive;
        _addonGamePath = addonGamePath;
        Names = events.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every soundevent name, sorted.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <param name="addonGamePath">The addon's game folder, whose own soundevents and sounds win over CS2's; null for CS2 only.</param>
    public static SoundLibrary Load(GameConnection connection, string? addonGamePath)
    {
        var events = new Dictionary<string, (KVObject, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in connection.Archive.AllPaths().Where(IsSoundEventsFile).Order(StringComparer.Ordinal))
        {
            Add(events, connection.Archive.ReadBytes(path), path);
        }

        var addonFolder = addonGamePath is null ? null : Path.Combine(addonGamePath, "soundevents");
        if (addonFolder is not null && Directory.Exists(addonFolder))
        {
            foreach (var file in Directory.EnumerateFiles(addonFolder, "*.vsndevts_c", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                Add(events, File.ReadAllBytes(file), Path.GetRelativePath(addonGamePath!, file).Replace('\\', '/'));
            }
        }

        return new SoundLibrary(events, connection.Archive, addonGamePath);
    }

    public bool Contains(string name) => _events.ContainsKey(name);

    /// <summary>The event with its <c>base</c> chain applied, or null when no soundevents file defines it.</summary>
    public SoundEventInfo? Find(string name)
    {
        if (!_events.TryGetValue(name, out var own))
        {
            return null;
        }

        // Nearest definition first: the event itself, then its base, its base's base…
        var chain = new List<KVObject> { own.Value };
        var current = own.Value;
        while (chain.Count < MaxBaseDepth && Text(current, "base") is { Length: > 0 } baseName && _events.TryGetValue(baseName, out var parent))
        {
            chain.Add(parent.Value);
            current = parent.Value;
        }

        IReadOnlyList<string> files = [];
        double? volume = null, pitch = null;
        foreach (var definition in chain)
        {
            if (files.Count == 0)
            {
                files = Files(definition);
            }

            volume ??= Number(definition, "volume");
            pitch ??= Number(definition, "pitch");
        }

        return new SoundEventInfo(name, files, volume ?? 1, pitch ?? 1, own.File);
    }

    /// <summary>Decodes a sound file (from the addon when it has its own, else CS2's), or null when it is missing or cannot be read.</summary>
    /// <param name="path">Resource path, e.g. <c>sounds/weapons/ssg08/ssg08_hit_01.vsnd</c>.</param>
    public DecodedSound? Decode(string path) => _decoded.GetOrAdd(path, Read);

    private DecodedSound? Read(string path)
    {
        var compiled = path.EndsWith("_c", StringComparison.Ordinal) ? path : path + "_c";
        byte[] bytes;
        var addonFile = _addonGamePath is null ? null : Path.Combine(_addonGamePath, compiled.Replace('/', Path.DirectorySeparatorChar));
        if (addonFile is not null && File.Exists(addonFile))
        {
            bytes = File.ReadAllBytes(addonFile);
        }
        else if (_archive.Contains(compiled))
        {
            bytes = _archive.ReadBytes(compiled);
        }
        else
        {
            return null;
        }

        try
        {
            using var resource = new Resource { FileName = compiled };
            resource.Read(new MemoryStream(bytes));
            if (resource.DataBlock is not Sound sound)
            {
                return null;
            }

            using var stream = sound.GetSoundStream();
            return new DecodedSound(path, stream.ToArray(), sound.SoundType == Sound.AudioFileType.MP3, sound.Duration);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException or NotImplementedException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsSoundEventsFile(string path) =>
        path.StartsWith(Folder, StringComparison.Ordinal) && path.EndsWith(".vsndevts_c", StringComparison.Ordinal);

    private static void Add(Dictionary<string, (KVObject, string)> events, byte[] bytes, string file)
    {
        try
        {
            using var resource = new Resource { FileName = file };
            resource.Read(new MemoryStream(bytes));
            if (resource.DataBlock is not BinaryKV3 data)
            {
                return;
            }

            foreach (var (name, value) in data.Data.Root.Children.Where(child => child.Value.IsCollection && !child.Value.IsArray))
            {
                events[name] = (value, file);
            }
        }
        catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException)
        {
            // A soundevents file this VRF cannot read is skipped; its events just cannot be auditioned.
        }
    }

    private static IReadOnlyList<string> Files(KVObject definition)
    {
        foreach (var key in new[] { "vsnd_files_track_01", "vsnd_files" })
        {
            if (!definition.TryGetValue(key, out var value))
            {
                continue;
            }

            return value.IsArray
                ? value.Values.Select(item => item.ToString() ?? "").Where(item => item.Length > 0).ToList()
                : [value.ToString() ?? ""];
        }

        return [];
    }

    private static string? Text(KVObject definition, string key) =>
        definition.TryGetValue(key, out var value) && !value.IsArray && !value.IsCollection ? value.ToString() : null;

    private static double? Number(KVObject definition, string key) =>
        definition.TryGetValue(key, out var value) && !value.IsArray && !value.IsCollection
        && double.TryParse(value.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
