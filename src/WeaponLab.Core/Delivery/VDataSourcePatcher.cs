using System.Globalization;
using System.Text.RegularExpressions;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Clips.Encoding;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Delivery;

/// <summary>
/// Edits an addon's <c>content/.../scripts/weapons.vdata</c> source in place:
/// each edited value's text is replaced, keeping its flag prefix and float
/// form; keys the source does not list yet are written from the compiled
/// data; keys a class change removes are deleted with their line. Comments,
/// formatting, order and hand edits that were never compiled all survive.
/// </summary>
public static partial class VDataSourcePatcher
{
    [GeneratedRegex(@"^(?<key>[^.\[\]]+)|\.(?<key>[^.\[\]]+)|\[(?<index>\d+)\]")]
    private static partial Regex PathSegment();

    private sealed record Edit(int Start, int End, string Text);

    /// <param name="source">The current source text.</param>
    /// <param name="compiled">Compiled vdata to write missing keys from (the addon's, else CS2's).</param>
    /// <param name="stock">CS2's compiled vdata: class change donors.</param>
    /// <param name="original">The addon's original compiled vdata, for swaps that restore its own values.</param>
    public static string Patch(
        string source,
        IReadOnlyList<(string Block, string Path, string Value)> values,
        IReadOnlyList<VDataKeySwap> swaps,
        byte[] compiled,
        byte[] stock,
        byte[]? original)
    {
        var root = Kv3Text.Parse(source);
        var compiledRoot = Root(compiled);
        var stockRoot = Root(stock);
        var originalRoot = original is null ? stockRoot : Root(original);
        var edits = new List<Edit>();

        var blocks = values.Select(value => value.Block).Concat(swaps.Select(swap => swap.Block)).Distinct(StringComparer.Ordinal);
        foreach (var blockName in blocks)
        {
            var block = root.Member(blockName) ?? throw new InvalidDataException(
                $"The addon's weapons.vdata source has no block '{blockName}'. Use 'Refresh from CS2' in the VData tab, or remove that edit.");
            var swapped = swaps.Where(swap => swap.Block == blockName)
                .SelectMany(swap => swap.Keys.Select(key => (Key: key, Donor: (swap.FromOriginal ? originalRoot : stockRoot)[swap.Donor])))
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Donor, StringComparer.Ordinal);
            var blockValues = values.Where(value => value.Block == blockName).ToList();

            foreach (var group in blockValues.GroupBy(value => VDataGlossary.RootKey(value.Path)).Select(group => group.Key).Union(swapped.Keys))
            {
                var rootKey = group;
                var existing = block.Member(rootKey);
                var groupValues = blockValues.Where(value => VDataGlossary.RootKey(value.Path) == rootKey).ToList();

                // Values on an existing key the class change does not replace: edit each value's text in place.
                if (!swapped.ContainsKey(rootKey) && existing is not null && TryEditInPlace(source, existing, rootKey, groupValues, compiledRoot[blockName], edits))
                {
                    continue;
                }

                // Otherwise write the whole key: from the donor (class change) or the compiled data, with the values applied.
                KVObject? template = swapped.TryGetValue(rootKey, out var donor)
                    ? (donor?.TryGetValue(rootKey, out var donorValue) == true ? donorValue : null)
                    : compiledRoot[blockName]?.TryGetValue(rootKey, out var compiledValue) == true ? compiledValue : null;
                if (template is null)
                {
                    if (existing is not null)
                    {
                        edits.Add(new Edit(LineStart(source, existing.KeyStart), LineEnd(source, existing.End), ""));
                    }

                    continue;
                }

                var holder = KVObject.Collection();
                holder[rootKey] = KVValues.Clone(template);
                foreach (var (_, path, value) in groupValues)
                {
                    VDataCompiler.SetLeaf(holder, path, value, blockName);
                }

                var text = Kv3Text.Write(holder[rootKey], 2);
                if (existing is not null)
                {
                    edits.Add(new Edit(existing.Start, existing.End, text));
                }
                else
                {
                    var insertAt = LineStart(source, block.End - 1);
                    edits.Add(new Edit(insertAt, insertAt, "\t\t" + rootKey + " = " + text + "\n"));
                }
            }
        }

        // Inserted text follows the file's own line endings.
        if (source.Contains("\r\n", StringComparison.Ordinal))
        {
            edits = edits.Select(edit => edit with { Text = edit.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) }).ToList();
        }

        var result = Apply(source, edits);
        Verify(result, values, swaps, stockRoot, originalRoot);
        return result;
    }

    private static KVObject Root(byte[] compiled)
    {
        using var resource = new Resource { FileName = VDataFile.GamePath };
        resource.Read(new MemoryStream(compiled));
        return (resource.DataBlock as BinaryKV3 ?? throw new InvalidDataException("weapons.vdata_c is not KV3.")).Data.Root;
    }

    /// <summary>Replaces each value's text; false when a value's path is not in the source (the whole key is written instead).</summary>
    private static bool TryEditInPlace(string source, Kv3Node existing, string rootKey, List<(string Block, string Path, string Value)> values, KVObject? compiledBlock, List<Edit> edits)
    {
        var planned = new List<Edit>();
        foreach (var (_, path, value) in values)
        {
            var node = Find(existing, rootKey, path);
            if (node is null || node.Kind != Kv3NodeKind.Scalar)
            {
                return false;
            }

            var type = compiledBlock is null ? KVValueType.String : CompiledType(compiledBlock, path);
            planned.Add(new Edit(node.Start, node.End, Kv3Text.FormatLike(source[node.Start..node.End], value, type)));
        }

        edits.AddRange(planned);
        return true;
    }

    /// <summary>The node at a flattened path such as <c>m_flSpread[1]</c> or <c>m_aShootSounds.WEAPON_SOUND_SINGLE</c>, starting at its root key's value.</summary>
    private static Kv3Node? Find(Kv3Node rootValue, string rootKey, string path)
    {
        var node = rootValue;
        foreach (Match segment in PathSegment().Matches(path[rootKey.Length..]))
        {
            if (segment.Groups["index"].Success)
            {
                var index = int.Parse(segment.Groups["index"].Value, CultureInfo.InvariantCulture);
                if (node.Kind != Kv3NodeKind.Array || index >= node.Items.Count)
                {
                    return null;
                }

                node = node.Items[index];
            }
            else
            {
                var child = node.Kind == Kv3NodeKind.Object ? node.Member(segment.Groups["key"].Value) : null;
                if (child is null)
                {
                    return null;
                }

                node = child;
            }
        }

        return node;
    }

    private static KVValueType CompiledType(KVObject block, string path)
    {
        var node = block;
        foreach (Match segment in PathSegment().Matches(path))
        {
            if (segment.Groups["index"].Success)
            {
                var index = int.Parse(segment.Groups["index"].Value, CultureInfo.InvariantCulture);
                if (!node.IsArray || index >= node.Count)
                {
                    return KVValueType.String;
                }

                node = node[index];
            }
            else if (!node.TryGetValue(segment.Groups["key"].Value, out node))
            {
                return KVValueType.String;
            }
        }

        // A float key the addon wrote as "1" compiles as an integer; m_fl keys are floats whatever the file says.
        return VDataGlossary.RootKey(path).StartsWith("m_fl", StringComparison.Ordinal) ? KVValueType.FloatingPoint : node.ValueType;
    }

    private static int LineStart(string text, int position)
    {
        var newline = text.LastIndexOf('\n', Math.Max(0, position - 1));
        return newline < 0 ? 0 : newline + 1;
    }

    private static int LineEnd(string text, int position)
    {
        var newline = text.IndexOf('\n', position);
        return newline < 0 ? text.Length : newline + 1;
    }

    private static string Apply(string source, List<Edit> edits)
    {
        var ordered = edits.OrderByDescending(edit => edit.Start).ThenByDescending(edit => edit.End).ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].End > ordered[index - 1].Start && ordered[index].Start != ordered[index - 1].Start)
            {
                throw new InvalidDataException("Internal error: overlapping edits to weapons.vdata.");
            }
        }

        var text = source;
        foreach (var edit in ordered)
        {
            text = text[..edit.Start] + edit.Text + text[edit.End..];
        }

        return text;
    }

    /// <summary>The patched text must parse, hold every value, and match the swaps (keys present or removed like the donor).</summary>
    public static void Verify(string text, IEnumerable<(string Block, string Path, string Value)> values, IEnumerable<VDataKeySwap> swaps, KVObject stockRoot, KVObject originalRoot)
    {
        var root = Kv3Text.Parse(text);
        try
        {
            KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Deserialize(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new InvalidDataException("The patched weapons.vdata does not parse as KV3: " + error.Message, error);
        }

        foreach (var (block, path, value) in values)
        {
            var node = root.Member(block) is { } blockNode && blockNode.Member(VDataGlossary.RootKey(path)) is { } rootValue ? Find(rootValue, VDataGlossary.RootKey(path), path) : null;
            var actual = node is null ? null : Kv3Text.ScalarValue(text, node);
            if (actual is null || !VDataValues.Same(actual, value))
            {
                throw new InvalidDataException($"The patched weapons.vdata has {block}.{path} = {actual ?? "(missing)"}, expected {value}.");
            }
        }

        foreach (var swap in swaps)
        {
            var donor = (swap.FromOriginal ? originalRoot : stockRoot)[swap.Donor];
            foreach (var key in swap.Keys)
            {
                var present = root.Member(swap.Block)?.Member(key) is not null;
                if (present != (donor?.ContainsKey(key) == true))
                {
                    throw new InvalidDataException($"The patched weapons.vdata's {swap.Block}.{key} does not follow {swap.Donor}.");
                }
            }
        }
    }
}
