using System.Text.Json;
using WeaponLab.Core.Profiles;

namespace WeaponLab.Core.VData;

/// <summary>
/// vdata values that differ from CS2: block → property path → value. Backed
/// by the active profile; every change is saved through the callback.
/// <see cref="Load(string)"/> still reads the 0.6–0.7 workspace file so it
/// can be moved into a profile.
/// </summary>
public sealed class VDataOverrides
{
    public const string LegacySchema = "weaponlab.vdata-overrides/1";

    private readonly SortedDictionary<string, SortedDictionary<string, string>> _blocks;
    private readonly Action _save;

    public VDataOverrides(SortedDictionary<string, SortedDictionary<string, string>> blocks, Action? save = null)
    {
        _blocks = blocks;
        _save = save ?? (() => { });
    }

    public static VDataOverrides For(Profile profile, Action save) => new(profile.VData, save);

    public static VDataOverrides Empty() => new(new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal));

    public static string PathFor(string workspacePath) => Path.Combine(workspacePath, "vdata", "overrides.json");

    /// <summary>Reads the legacy workspace file; empty when it does not exist.</summary>
    public static VDataOverrides Load(string workspacePath)
    {
        var blocks = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        var path = PathFor(workspacePath);
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.GetProperty("schema").GetString() != LegacySchema)
            {
                throw new InvalidDataException($"Unsupported vdata overrides file: {path}");
            }

            foreach (var block in document.RootElement.GetProperty("blocks").EnumerateObject())
            {
                var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var value in block.Value.EnumerateObject())
                {
                    values[value.Name] = value.Value.GetString() ?? "";
                }

                blocks[block.Name] = values;
            }
        }

        return new VDataOverrides(blocks);
    }

    public IReadOnlyDictionary<string, string> For(string block) =>
        _blocks.TryGetValue(block, out var values) ? values : new SortedDictionary<string, string>();

    public int Count => _blocks.Values.Sum(values => values.Count);

    public IEnumerable<(string Block, string Path, string Value)> All() =>
        _blocks.SelectMany(block => block.Value.Select(value => (block.Key, value.Key, value.Value))).ToList();

    /// <summary>Sets a value; one equal to stock removes the override instead.</summary>
    public void Set(string block, string path, string value, string stockValue)
    {
        if (value == stockValue)
        {
            Remove(block, path);
            return;
        }

        if (!_blocks.TryGetValue(block, out var values))
        {
            _blocks[block] = values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        values[path] = value;
        _save();
    }

    public void Remove(string block, string path)
    {
        if (_blocks.TryGetValue(block, out var values) && values.Remove(path))
        {
            if (values.Count == 0)
            {
                _blocks.Remove(block);
            }

            _save();
        }
    }

    public void ClearBlock(string block)
    {
        if (_blocks.Remove(block))
        {
            _save();
        }
    }
}
