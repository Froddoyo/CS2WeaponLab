using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace WeaponLab.Core.VData;

/// <summary>One leaf value of a vdata block, e.g. <c>m_flMaxSpeed[0] = 215</c>.</summary>
/// <param name="Path">Dotted path with [index] for arrays: <c>m_aShootSounds.WEAPON_SOUND_SINGLE</c>.</param>
public sealed record VDataProperty(string Path, string Value, KVValueType Type);

/// <summary>A named block such as <c>weapon_ak47</c> or the <c>rifle</c> base.</summary>
public sealed record VDataBlock(string Name, string? Base, string? Class, IReadOnlyList<VDataProperty> Properties)
{
    public const string BaseKey = "_base";
    public const string ClassKey = "_class";
    public const string SkeletonKey = "m_szAnimSkeleton";
    public const string WorldModelKey = "m_szWorldModel";

    public string? Value(string path) => Properties.FirstOrDefault(property => property.Path == path)?.Value;

    public bool IsWeapon => Class is not null && Name.StartsWith("weapon_", StringComparison.Ordinal);
}

/// <summary>
/// A compiled <c>weapons.vdata_c</c>: named blocks of weapon properties.
/// Compiled blocks hold their full values plus <c>_base</c>, so inheritance
/// is found by comparing a block with its base chain.
/// </summary>
public sealed class VDataFile
{
    public const string GamePath = "scripts/weapons.vdata_c";

    private readonly Dictionary<string, VDataBlock> _blocks;

    private VDataFile(string genericDataType, List<VDataBlock> blocks)
    {
        GenericDataType = genericDataType;
        Blocks = blocks;
        _blocks = blocks.ToDictionary(block => block.Name, StringComparer.Ordinal);
    }

    public string GenericDataType { get; }
    public IReadOnlyList<VDataBlock> Blocks { get; }

    public VDataBlock? this[string name] => _blocks.GetValueOrDefault(name);

    /// <summary>A vdata file made of blocks read elsewhere (the addon's source text, see <see cref="AddonVData"/>).</summary>
    public static VDataFile FromBlocks(string genericDataType, IEnumerable<VDataBlock> blocks) => new(genericDataType, blocks.ToList());

    public static VDataFile Read(byte[] compiledBytes, string fileName = GamePath)
    {
        using var resource = new Resource { FileName = fileName };
        try
        {
            resource.Read(new MemoryStream(compiledBytes));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Cannot decode {fileName}: {error.Message}", error);
        }

        if (resource.DataBlock is not BinaryKV3 data)
        {
            throw new InvalidDataException($"{fileName} is not a KV3 vdata file.");
        }

        var root = data.Data.Root;
        var genericType = root.TryGetValue("generic_data_type", out var type) ? type.ToString() ?? "" : "";
        var blocks = root.Children
            .Where(child => child.Value.IsCollection)
            .Select(child => ReadBlock(child.Key, child.Value))
            .ToList();
        return new VDataFile(genericType, blocks);
    }

    private static VDataBlock ReadBlock(string name, KVObject value)
    {
        var properties = new List<VDataProperty>();
        foreach (var (key, child) in value.Children)
        {
            Flatten(key, child, properties);
        }

        string? Text(string key) => value.TryGetValue(key, out var item) ? item.ToString() : null;
        return new VDataBlock(name, Text(VDataBlock.BaseKey), Text(VDataBlock.ClassKey), properties);
    }

    private static void Flatten(string path, KVObject value, List<VDataProperty> output)
    {
        if (value.IsArray)
        {
            for (var index = 0; index < value.Count; index++)
            {
                Flatten($"{path}[{index}]", value[index], output);
            }
        }
        else if (value.IsCollection)
        {
            foreach (var (key, child) in value.Children)
            {
                Flatten($"{path}.{key}", child, output);
            }
        }
        else
        {
            output.Add(new VDataProperty(path, Format(value), value.ValueType));
        }
    }

    private static string Format(KVObject value)
    {
        return value.ValueType switch
        {
            KVValueType.Boolean => value.ToBoolean(CultureInfo.InvariantCulture) ? "true" : "false",
            KVValueType.FloatingPoint or KVValueType.FloatingPoint64 => value.ToDouble(CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            KVValueType.Null => "",
            _ => value.ToString(CultureInfo.InvariantCulture) ?? "",
        };
    }

    /// <summary>The block's base chain, nearest first: weapon_ak47 → rifle → primary → weapon_base.</summary>
    public IReadOnlyList<VDataBlock> BaseChain(VDataBlock block)
    {
        var chain = new List<VDataBlock>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { block.Name };
        for (var current = block.Base is null ? null : this[block.Base]; current is not null; current = current.Base is null ? null : this[current.Base])
        {
            if (!seen.Add(current.Name))
            {
                break; // A cycle would be a broken file; stop rather than loop.
            }

            chain.Add(current);
        }

        return chain;
    }

    /// <summary>Weapon blocks that animate with this skeleton, e.g. ak47.vnmskel → weapon_ak47.</summary>
    public IReadOnlyList<VDataBlock> BlocksForSkeleton(string skeletonPath)
    {
        return Blocks
            .Where(block => block.IsWeapon && string.Equals(block.Value(VDataBlock.SkeletonKey), skeletonPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
