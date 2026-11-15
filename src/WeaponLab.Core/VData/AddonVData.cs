using ValveKeyValue;
using WeaponLab.Core.Delivery;

namespace WeaponLab.Core.VData;

/// <summary>
/// The addon's vdata as its SOURCE file says (user decision 2026-11-07: the
/// source is the addon for green and yellow, so an edit saved outside Weapon
/// Lab, in Visual Studio say, is the addon's own as soon as it is saved, before
/// compiling). The game still runs the compiled file until it is compiled.
/// </summary>
public static class AddonVData
{
    /// <summary>
    /// The compiled values with every value the source text sets on top,
    /// resolved through each block's <c>_base</c> chain inside the source (the
    /// compiled file holds full blocks; the source only what each block sets).
    /// The compiled file alone when there is no source, null when there is neither.
    /// </summary>
    public static VDataFile? Read(AddonTarget target)
    {
        var compiled = File.Exists(target.VDataCompiledPath) ? VDataFile.Read(File.ReadAllBytes(target.VDataCompiledPath), target.VDataCompiledPath) : null;
        if (!File.Exists(target.VDataSourcePath))
        {
            return compiled;
        }

        return Merge(compiled, File.ReadAllText(target.VDataSourcePath));
    }

    /// <summary>When either file last changed, to know when to read again.</summary>
    public static DateTime Stamp(AddonTarget target) =>
        new[] { target.VDataCompiledPath, target.VDataSourcePath }.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();

    /// <summary>True when the source was saved after the last compile: the game still runs older values.</summary>
    public static bool SourceIsNewer(AddonTarget target) =>
        File.Exists(target.VDataSourcePath) && File.Exists(target.VDataCompiledPath)
        && File.GetLastWriteTimeUtc(target.VDataSourcePath) > File.GetLastWriteTimeUtc(target.VDataCompiledPath).AddSeconds(2);

    /// <param name="compiled">The compiled file the source values go on top of, or null.</param>
    /// <param name="sourceText">The .vdata source (KV3 text).</param>
    public static VDataFile Merge(VDataFile? compiled, string sourceText)
    {
        var root = Kv3Text.Parse(sourceText);
        var source = new Dictionary<string, SourceBlock>(StringComparer.Ordinal);
        foreach (var (name, node) in root.Members.Where(member => member.Value.Kind == Kv3NodeKind.Object))
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string? baseName = null;
            string? className = null;
            foreach (var (key, value) in node.Members)
            {
                if (key == VDataBlock.BaseKey && value.Kind == Kv3NodeKind.Scalar)
                {
                    baseName = Kv3Text.ScalarValue(sourceText, value);
                }
                else if (key == VDataBlock.ClassKey && value.Kind == Kv3NodeKind.Scalar)
                {
                    className = Kv3Text.ScalarValue(sourceText, value);
                }
                else
                {
                    Flatten(sourceText, key, value, values);
                }
            }

            source[name] = new SourceBlock(baseName, className, values);
        }

        var genericType = root.Member("generic_data_type") is { Kind: Kv3NodeKind.Scalar } type ? Kv3Text.ScalarValue(sourceText, type) : compiled?.GenericDataType ?? "";
        var blocks = new List<VDataBlock>();
        var names = (compiled?.Blocks.Select(block => block.Name) ?? []).Concat(source.Keys).Distinct(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var full = compiled?[name];
            if (!source.TryGetValue(name, out var own))
            {
                blocks.Add(full!);
                continue;
            }

            // The block's values, then each base's in turn for what it does not set.
            var resolved = new Dictionary<string, string>(own.Values, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal) { name };
            for (var baseName = own.Base; baseName is not null && seen.Add(baseName) && source.TryGetValue(baseName, out var baseBlock); baseName = baseBlock.Base)
            {
                foreach (var (path, value) in baseBlock.Values)
                {
                    resolved.TryAdd(path, value);
                }
            }

            var properties = new List<VDataProperty>();
            foreach (var property in full?.Properties ?? [])
            {
                properties.Add(resolved.Remove(property.Path, out var value) ? property with { Value = value } : property);
            }

            properties.AddRange(resolved.Select(item => new VDataProperty(item.Key, item.Value, Guess(item.Value))));
            blocks.Add(new VDataBlock(name, own.Base ?? full?.Base, own.Class ?? full?.Class, properties));
        }

        return VDataFile.FromBlocks(genericType, blocks);
    }

    private sealed record SourceBlock(string? Base, string? Class, Dictionary<string, string> Values);

    /// <summary>Paths as the compiled reader writes them: dotted keys, [index] for array items.</summary>
    private static void Flatten(string text, string path, Kv3Node node, Dictionary<string, string> output)
    {
        switch (node.Kind)
        {
            case Kv3NodeKind.Object:
                foreach (var (key, child) in node.Members)
                {
                    Flatten(text, $"{path}.{key}", child, output);
                }

                break;
            case Kv3NodeKind.Array:
                for (var index = 0; index < node.Items.Count; index++)
                {
                    Flatten(text, $"{path}[{index}]", node.Items[index], output);
                }

                break;
            default:
                output[path] = Kv3Text.ScalarValue(text, node) switch
                {
                    "null" => "",
                    var value => value,
                };
                break;
        }
    }

    private static KVValueType Guess(string value) =>
        value is "true" or "false" ? KVValueType.Boolean
        : long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _) ? KVValueType.Int64
        : double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) ? KVValueType.FloatingPoint
        : KVValueType.String;
}
