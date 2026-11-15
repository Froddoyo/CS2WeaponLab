using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Clips.Encoding;
using WeaponLab.Core.Game;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Delivery;

public sealed class VDataCompileException(string message, string log) : Exception(message)
{
    public string Log { get; } = log;
}

/// <summary>
/// Writes the game's current weapons.vdata plus your overrides as KV3 text,
/// and compiles it into an addon with the Workshop Tools resourcecompiler.
/// The addon file replaces the whole game file when mounted, so it always
/// starts from the current stock data rather than an addon's old copy.
/// </summary>
public static partial class VDataCompiler
{
    public const string ContentRelativePath = "scripts/weapons.vdata";

    [GeneratedRegex(@"^(?<key>[^.\[\]]+)|\.(?<key>[^.\[\]]+)|\[(?<index>\d+)\]")]
    private static partial Regex PathSegment();

    /// <returns>The KV3 text and how many overrides were applied.</returns>
    public static (string Text, int Applied) BuildText(byte[] stockBytes, VDataOverrides overrides) => BuildText(stockBytes, overrides.All());

    /// <summary>
    /// Writes <paramref name="baseBytes"/> (a compiled weapons.vdata_c: the
    /// addon's own or the game's) as KV3 text with <paramref name="values"/> set.
    /// </summary>
    public static (string Text, int Applied) BuildText(byte[] baseBytes, IEnumerable<(string Block, string Path, string Value)> values) =>
        BuildText(baseBytes, values, [], null);

    /// <summary>
    /// As above, first replacing whole top-level keys of blocks with the
    /// donor's (class changes, and their resets). Each swap names the target
    /// block, the donor block in <paramref name="donorBytes"/> (CS2's file, or
    /// <paramref name="originalBytes"/> for swaps that restore the addon's own
    /// values) and the keys to replace; a key the donor lacks is removed.
    /// </summary>
    public static (string Text, int Applied) BuildText(
        byte[] baseBytes,
        IEnumerable<(string Block, string Path, string Value)> values,
        IEnumerable<VDataKeySwap> swaps,
        byte[]? donorBytes,
        byte[]? originalBytes = null)
    {
        var data = ReadKV3(baseBytes);
        var swapList = swaps.ToList();
        if (swapList.Count > 0)
        {
            var stockDonors = ReadKV3(donorBytes ?? throw new ArgumentNullException(nameof(donorBytes))).Data.Root;
            var originalDonors = originalBytes is null ? stockDonors : ReadKV3(originalBytes).Data.Root;
            foreach (var swap in swapList)
            {
                var target = data.Data.Root.TryGetValue(swap.Block, out var found) ? found
                    : throw new InvalidDataException($"The addon's vdata has no block '{swap.Block}'. Use 'Refresh from CS2' in the VData tab.");
                var donors = swap.FromOriginal ? originalDonors : stockDonors;
                var donor = donors.TryGetValue(swap.Donor, out var donorBlock) ? donorBlock
                    : throw new InvalidDataException($"CS2's vdata has no block '{swap.Donor}' to base {swap.Block} on.");
                foreach (var key in swap.Keys)
                {
                    if (donor.TryGetValue(key, out var value))
                    {
                        target[key] = value;
                    }
                    else
                    {
                        target.Remove(key);
                    }
                }
            }
        }

        var applied = 0;
        foreach (var (block, path, value) in values)
        {
            var blockValue = data.Data.Root.TryGetValue(block, out var found) ? found
                : throw new InvalidDataException($"The addon's vdata has no block '{block}'. Use 'Refresh from CS2' in the VData tab, or remove that edit.");
            SetLeaf(blockValue, path, value, block);
            applied++;
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using (var indented = new IndentedTextWriter(writer))
        {
            data.WriteText(indented);
        }

        return (writer.ToString(), applied);
    }

    private static BinaryKV3 ReadKV3(byte[] bytes)
    {
        using var resource = new Resource { FileName = VDataFile.GamePath };
        resource.Read(new MemoryStream(bytes));
        return resource.DataBlock as BinaryKV3 ?? throw new InvalidDataException("weapons.vdata_c is not KV3.");
    }

    /// <summary>Sets a flattened path such as <c>m_flSpread[1]</c> inside a block, keeping the value's type.</summary>
    internal static void SetLeaf(KVObject block, string path, string value, string blockName)
    {
        var segments = PathSegment().Matches(path).ToList();
        if (segments.Count == 0 || string.Concat(segments.Select(segment => segment.Value)) != path)
        {
            throw new InvalidDataException($"Unsupported vdata path '{path}'.");
        }

        KVObject parent = block;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var last = index == segments.Count - 1;
            if (segment.Groups["index"].Success)
            {
                var position = int.Parse(segment.Groups["index"].Value, CultureInfo.InvariantCulture);
                if (!parent.IsArray || position >= parent.Count)
                {
                    throw new InvalidDataException($"{blockName}.{path} does not exist in the current game.");
                }

                if (last)
                {
                    parent.AsArraySpan()[position] = Typed(parent[position], value, $"{blockName}.{path}");
                    return;
                }

                parent = parent[position];
            }
            else
            {
                var key = segment.Groups["key"].Value;
                if (!parent.TryGetValue(key, out var child))
                {
                    throw new InvalidDataException($"{blockName}.{path} does not exist in the current game.");
                }

                if (last)
                {
                    parent[key] = Typed(child, value, $"{blockName}.{path}");
                    return;
                }

                parent = child;
            }
        }
    }

    // The override was validated against this type when it was entered.
    private static KVObject Typed(KVObject original, string value, string label)
    {
        return original.ValueType switch
        {
            KVValueType.Boolean => new KVObject(value == "true") { Flag = original.Flag },
            KVValueType.String => new KVObject(value) { Flag = original.Flag },
            KVValueType.FloatingPoint or KVValueType.FloatingPoint64 or KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64
                or KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64 =>
                KVValues.Like(original, double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            _ => throw new InvalidDataException($"{label}: {original.ValueType} values cannot be written."),
        };
    }

    /// <summary>
    /// Compiles <paramref name="contentFile"/> (inside the addon's content
    /// folder) to the matching game file and returns the compiler output.
    /// </summary>
    public static async Task<string> CompileAsync(GameInstall install, string contentFile, CancellationToken cancellation = default)
    {
        if (!install.HasWorkshopTools)
        {
            throw new VDataCompileException("CS2 Workshop Tools are not installed, so vdata cannot be compiled.", "");
        }

        var start = new ProcessStartInfo(install.ResourceCompilerPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(install.ResourceCompilerPath)!,
        };
        foreach (var argument in new[] { "-nop4", "-fshallow", "-i", contentFile })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new VDataCompileException("Could not start resourcecompiler.", "");
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var errors = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);
        var log = await output + await errors;
        if (process.ExitCode != 0)
        {
            throw new VDataCompileException($"resourcecompiler failed with exit code {process.ExitCode}.", log);
        }

        return log;
    }

    /// <summary>Checks the compiled file holds every override.</summary>
    public static void VerifyCompiled(byte[] compiledBytes, VDataOverrides overrides) => VerifyCompiled(compiledBytes, overrides.All());

    /// <summary>Checks the compiled file holds every value.</summary>
    public static void VerifyCompiled(byte[] compiledBytes, IEnumerable<(string Block, string Path, string Value)> values) =>
        VerifyCompiled(compiledBytes, values, [], null);

    /// <summary>Checks every value, and that swapped keys hold the donor's values (except paths a value sets).</summary>
    public static void VerifyCompiled(
        byte[] compiledBytes,
        IEnumerable<(string Block, string Path, string Value)> values,
        IEnumerable<VDataKeySwap> swaps,
        VDataFile? donors,
        VDataFile? originals = null)
    {
        var compiled = VDataFile.Read(compiledBytes, "compiled weapons.vdata_c");
        var valueList = values.ToList();
        foreach (var swap in swaps)
        {
            var set = valueList.Where(item => item.Block == swap.Block).Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
            var actual = compiled[swap.Block] ?? throw new InvalidDataException($"The compiled vdata has no block {swap.Block}.");
            var donor = (swap.FromOriginal ? originals ?? donors : donors)?[swap.Donor] ?? throw new InvalidDataException($"No vdata block {swap.Donor} to compare {swap.Block} with.");
            var keys = swap.Keys.ToHashSet(StringComparer.Ordinal);
            if (!VDataClassSwap.Matches(VDataClassSwap.Without(actual, set), VDataClassSwap.Without(donor, set), keys))
            {
                throw new InvalidDataException($"The compiled vdata's {swap.Block} does not hold {swap.Donor}'s values for {string.Join(", ", swap.Keys)}.");
            }
        }

        foreach (var (block, path, value) in valueList)
        {
            var actual = compiled[block]?.Value(path);
            if (actual is null || !SameValue(actual, value))
            {
                throw new InvalidDataException($"The compiled vdata has {block}.{path} = {actual ?? "(missing)"}, expected {value}.");
            }
        }
    }

    // Floats can be stored at lower precision after compiling.
    private static bool SameValue(string actual, string expected)
    {
        if (actual == expected)
        {
            return true;
        }

        return double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
            && Math.Abs(a - b) <= Math.Max(1e-6, Math.Abs(b) * 1e-6);
    }
}

/// <summary>Replace <paramref name="Keys"/> of <paramref name="Block"/> with <paramref name="Donor"/>'s (from CS2's vdata).</summary>
/// <remarks>A reset is a swap whose donor is the block itself.</remarks>
/// <param name="FromOriginal">Take the donor from the addon's original vdata instead of CS2's (restoring keys).</param>
public sealed record VDataKeySwap(string Block, string Donor, IReadOnlyList<string> Keys, bool FromOriginal = false);
