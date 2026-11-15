using System.Globalization;
using ValveKeyValue;

namespace WeaponLab.Core.VData;

/// <summary>Where a block's value comes from.</summary>
public enum VDataSource
{
    /// <summary>Same value as the nearest base that has the property.</summary>
    Inherited,

    /// <summary>Differs from the base, or the base lacks the property.</summary>
    SetHere,
}

/// <param name="InheritedFrom">The base block the value matches, when inherited.</param>
/// <param name="AddonValue">The chosen addon's value, when it overrides this block.</param>
/// <param name="Override">Your pending edit from the override store, if any.</param>
public sealed record VDataRow(
    string Path,
    string StockValue,
    KVValueType Type,
    VDataSource Source,
    string? InheritedFrom,
    string? AddonValue,
    string? Override)
{
    /// <summary>The value a class change gives this property; null when no class change covers it.</summary>
    public string? ClassValue { get; init; }

    /// <summary>The class change's donor lacks this property, so it is removed.</summary>
    public bool RemovedByClass { get; init; }

    /// <summary>
    /// The addon's own value: as it was before Weapon Lab changed the key, or its current value for a key
    /// Weapon Lab never wrote (an edit made outside Weapon Lab); null when the addon has no vdata (CS2's applies).
    /// </summary>
    public string? AddonOriginal { get; init; }

    /// <summary>
    /// What the addon gets when the profile sets nothing here: the class
    /// change's value, else the addon's own, else CS2's.
    /// </summary>
    public string Baseline => ClassValue ?? AddonOriginal ?? StockValue;

    public bool AddonDiffers => AddonValue is not null && AddonValue != StockValue;

    public string EffectiveValue => Override ?? Baseline;

    /// <summary>Applying the profile would change the addon here.</summary>
    public bool IsPending => AddonValue is not null ? !VDataValues.Same(AddonValue, EffectiveValue) : EffectiveValue != StockValue;
}

/// <summary>
/// Combines a stock block, its inheritance, an addon override file and your
/// pending edits into rows for display.
/// </summary>
public static class VDataView
{
    /// <param name="classSwap">The profile's class change for this block, if any.</param>
    /// <param name="original">The addon's vdata before Weapon Lab changed it (see AddonOriginals); null = CS2's.</param>
    /// <param name="writtenByLab">
    /// Paths of this block Weapon Lab wrote into the addon (the delivery manifest). Only these start from
    /// <paramref name="original"/>; any other key starts from the addon's current value, so an edit made
    /// outside Weapon Lab is the addon's own and not something Weapon Lab still has to write (bug 2026-11-07).
    /// Null keeps the old behaviour (every key from the original).
    /// </param>
    public static IReadOnlyList<VDataRow> Rows(
        VDataFile stock,
        VDataBlock block,
        VDataFile? addon,
        IReadOnlyDictionary<string, string>? overrides,
        VDataClassSwap? classSwap = null,
        VDataFile? original = null,
        IReadOnlyCollection<string>? writtenByLab = null)
    {
        var originalBlock = original?[block.Name];
        var chain = stock.BaseChain(block);
        var addonBlock = addon?[block.Name];
        var donor = classSwap is null ? null : stock[classSwap.From];
        var keys = donor is null ? [] : classSwap!.RootKeys(block, donor).ToHashSet(StringComparer.Ordinal);
        bool Covered(string path) => keys.Contains(VDataGlossary.RootKey(path));

        var rows = new List<VDataRow>(block.Properties.Count);
        foreach (var property in block.Properties)
        {
            var baseMatch = chain.Select(baseBlock => (Block: baseBlock, Value: baseBlock.Value(property.Path))).FirstOrDefault(item => item.Value is not null);
            var inherited = baseMatch.Value is not null && baseMatch.Value == property.Value;
            var classValue = Covered(property.Path) ? donor!.Value(property.Path) : null;
            rows.Add(new VDataRow(
                property.Path,
                property.Value,
                property.Type,
                inherited ? VDataSource.Inherited : VDataSource.SetHere,
                inherited ? baseMatch.Block.Name : null,
                addonBlock?.Value(property.Path),
                overrides?.GetValueOrDefault(property.Path))
            {
                ClassValue = classValue,
                RemovedByClass = Covered(property.Path) && classValue is null,
                AddonOriginal = writtenByLab is null || writtenByLab.Contains(property.Path)
                    ? originalBlock?.Value(property.Path)
                    : addonBlock?.Value(property.Path) ?? originalBlock?.Value(property.Path),
            });
        }

        // Properties only the donor has, e.g. taxonomy.smg on a pistol based on an SMG.
        foreach (var property in donor?.Properties.Where(property => Covered(property.Path) && block.Value(property.Path) is null) ?? [])
        {
            rows.Add(new VDataRow(property.Path, "—", property.Type, VDataSource.SetHere, null, addonBlock?.Value(property.Path), overrides?.GetValueOrDefault(property.Path))
            {
                ClassValue = property.Value,
            });
        }

        return rows;
    }

    /// <summary>
    /// Checks a typed edit against the property's original type and returns
    /// it in canonical form, or an error message.
    /// </summary>
    public static (string? Value, string? Error) Normalize(KVValueType type, string input)
    {
        var text = input.Trim();
        switch (type)
        {
            case KVValueType.Boolean:
                return text.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" => ("true", null),
                    "false" or "0" or "no" => ("false", null),
                    _ => (null, "Use true or false."),
                };
            case KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64:
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    ? (whole.ToString(CultureInfo.InvariantCulture), null)
                    : (null, "Use a whole number.");
            case KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64:
                return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var positive)
                    ? (positive.ToString(CultureInfo.InvariantCulture), null)
                    : (null, "Use a whole number of 0 or more.");
            case KVValueType.FloatingPoint or KVValueType.FloatingPoint64:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
                    ? (number.ToString("R", CultureInfo.InvariantCulture), null)
                    : (null, "Use a number like 0.25.");
            case KVValueType.String:
                return (text, null);
            default:
                return (null, $"{type} values cannot be edited yet.");
        }
    }
}
