using ValveKeyValue;

namespace WeaponLab.Core.VData;

/// <summary>
/// Values to offer in a selector for a vdata property: true/false for
/// booleans, and for text properties every value the game's and the addon's
/// vdata already use at that path (weapon types, gear slots, models, sounds,
/// skeletons…). Numbers get no list. The editor still accepts typed values.
/// </summary>
public sealed class VDataChoices
{
    private static readonly IReadOnlyList<string> Booleans = ["true", "false"];

    private readonly Dictionary<string, SortedSet<string>> _text = new(StringComparer.Ordinal);

    private VDataChoices()
    {
    }

    /// <param name="extra">More values per path, e.g. the addon's own weapon models for m_szWorldModel.</param>
    public static VDataChoices Build(VDataFile stock, VDataFile? addon, IReadOnlyDictionary<string, IEnumerable<string>>? extra = null)
    {
        var choices = new VDataChoices();
        foreach (var file in addon is null ? [stock] : new[] { stock, addon })
        {
            foreach (var property in file.Blocks.SelectMany(block => block.Properties).Where(property => property.Type == KVValueType.String))
            {
                choices.Add(property.Path, property.Value);
            }
        }

        foreach (var (path, values) in extra ?? new Dictionary<string, IEnumerable<string>>())
        {
            foreach (var value in values)
            {
                choices.Add(path, value);
            }
        }

        return choices;
    }

    private void Add(string path, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (!_text.TryGetValue(path, out var set))
        {
            _text[path] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        set.Add(value);
    }

    /// <summary>The choices for a property, or null when it has none (numbers).</summary>
    public IReadOnlyList<string>? For(string path, KVValueType type) => type switch
    {
        KVValueType.Boolean => Booleans,
        KVValueType.String when _text.TryGetValue(path, out var set) => set.ToList(),
        _ => null,
    };
}
