using WeaponLab.Core.Game;

namespace WeaponLab.Core.Blender;

/// <param name="Key">"game:&lt;vpk path&gt;" or "addon:&lt;addon&gt;:&lt;path&gt;", as stored in a profile.</param>
/// <param name="Label">Shown in the picker, e.g. "cs_hl_template · weapon_1911".</param>
public sealed record PreviewModel(string Key, string Label);

/// <param name="Compiled">When the compiled model was written (local time).</param>
/// <param name="Source">The model's <c>.vmdl</c> source in the addon's content folder, if found.</param>
/// <param name="SourceSaved">When that source was last saved.</param>
public sealed record ModelFreshness(DateTime Compiled, string? Source, DateTime? SourceSaved)
{
    /// <summary>The source was saved after the compile, so Weapon Lab would use an older build.</summary>
    public bool SourceIsNewer => SourceSaved is { } saved && saved > Compiled.AddSeconds(2);

    /// <summary>One line for the model pickers.</summary>
    public string Describe(string modelName) => SourceIsNewer
        ? $"⚠ {Path.GetFileName(Source)} was saved {SourceSaved:yyyy-MM-dd HH:mm}, after the compile ({Compiled:yyyy-MM-dd HH:mm}): compile it again in ModelDoc, or Weapon Lab uses the older build."
        : $"Compiled {Compiled:yyyy-MM-dd HH:mm}. Weapon Lab uses the compiled {modelName}: after changing the model, compile it in ModelDoc first.";
}

/// <summary>
/// Models that can stand in for a weapon's stock mesh in Blender: the
/// game's weapon models and every compiled model in the installed addons
/// (e.g. cs_hl_template's custom weapons).
/// </summary>
public static class PreviewModels
{
    public const string GamePrefix = "game:";
    public const string AddonPrefix = "addon:";

    public static IReadOnlyList<PreviewModel> List(GameConnection connection)
    {
        var models = new List<PreviewModel>();
        foreach (var addon in connection.Addons)
        {
            if (!Directory.Exists(addon.GamePath))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(addon.GamePath, "*.vmdl_c", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(addon.GamePath, file).Replace('\\', '/');
                if (relative.StartsWith("models/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("weapons/", StringComparison.OrdinalIgnoreCase))
                {
                    models.Add(new PreviewModel($"{AddonPrefix}{addon.Name}:{relative}", $"{addon.Name} · {Path.GetFileNameWithoutExtension(relative)}"));
                }
            }
        }

        models.AddRange(connection.Archive.AllPaths()
            .Where(path => path.StartsWith("weapons/models/", StringComparison.Ordinal) && path.EndsWith(".vmdl_c", StringComparison.Ordinal)
                && !path.Contains("_mag.", StringComparison.Ordinal) && !path.Contains("_world", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => new PreviewModel(GamePrefix + path, "CS2 · " + Path.GetFileNameWithoutExtension(path))));
        return models;
    }

    /// <summary>
    /// The model key for a compiled <c>.vmdl_c</c> picked in an addon's game
    /// folder. Weapon Lab reads compiled models only (user decision
    /// 2026-10-29): a <c>.vmdl</c> source used to stand for its last compile,
    /// which silently used a stale model. A typed key (<c>game:...</c> or
    /// <c>addon:...</c>) is passed through.
    /// </summary>
    public static string FromFile(GameConnection connection, string input)
    {
        var text = input.Trim().Trim('"');
        if (text.StartsWith(GamePrefix, StringComparison.Ordinal) || text.StartsWith(AddonPrefix, StringComparison.Ordinal))
        {
            return text;
        }

        var file = Path.GetFullPath(text);
        if (file.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Pick the compiled {Path.GetFileName(file)}_c in game\\csgo_addons\\<addon>\\…: Weapon Lab uses compiled models only. Compile the model in ModelDoc first.");
        }

        if (!file.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Pick a compiled .vmdl_c model.");
        }

        var addonsRoot = Path.GetFullPath(connection.Install.GameAddonsPath) + Path.DirectorySeparatorChar;
        var relative = file.StartsWith(addonsRoot, StringComparison.OrdinalIgnoreCase) ? file[addonsRoot.Length..] : null;
        var slash = relative?.IndexOf(Path.DirectorySeparatorChar) ?? -1;
        if (relative is null || slash < 0)
        {
            throw new ArgumentException("Pick a compiled model inside an addon's game folder (game\\csgo_addons\\<addon>); CS2's own models are the Default choice.");
        }

        if (!File.Exists(file))
        {
            throw new FileNotFoundException($"{file} does not exist.");
        }

        return $"{AddonPrefix}{relative[..slash]}:{relative[(slash + 1)..].Replace('\\', '/')}";
    }

    /// <summary>
    /// When an addon model was compiled, and whether its <c>.vmdl</c> source
    /// (same path in the addon's content folder) was saved later. Null for
    /// CS2's own models and keys that do not resolve (never throws).
    /// </summary>
    public static ModelFreshness? Freshness(GameConnection connection, string key)
    {
        (AddonInfo Addon, string File)? resolved;
        try
        {
            resolved = ResolveAddon(connection, key);
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or InvalidDataException)
        {
            return null; // A half-typed or unknown key: nothing to say yet.
        }

        if (resolved is not { } found || !File.Exists(found.File))
        {
            return null;
        }

        var inside = Path.GetRelativePath(found.Addon.GamePath, found.File);
        var source = Path.Combine(connection.Install.ContentAddonsPath, found.Addon.Name, inside.EndsWith("_c", StringComparison.OrdinalIgnoreCase) ? inside[..^2] : inside);
        return new ModelFreshness(File.GetLastWriteTime(found.File), File.Exists(source) ? source : null, File.Exists(source) ? File.GetLastWriteTime(source) : null);
    }

    /// <summary>CS2's weapon model that targets a weapon skeleton (what Blender shows by default), e.g. weapons/models/ssg08/weapon_snip_ssg08.vmdl_c; null if none.</summary>
    public static string? StockModelFor(GameConnection connection, string weaponSkeleton)
    {
        using var exporter = new PreviewExporter(connection.Install);
        return exporter.FindWeaponModel(weaponSkeleton);
    }

    /// <summary>The addon folder and file for an addon key, or null for a game model.</summary>
    /// <summary>
    /// The preview key of the model a vdata value names (<c>models/…/x.vmdl</c>, e.g. m_szWorldModel): the
    /// addon's compiled copy when it has one, else CS2's; null when neither has it compiled.
    /// </summary>
    public static string? ForVDataModel(GameConnection connection, string? addonName, string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return null;
        }

        var path = modelPath.Trim().Trim('"').Replace('\\', '/');
        var compiled = path.EndsWith("_c", StringComparison.OrdinalIgnoreCase) ? path : path + "_c";
        var addon = addonName is null ? null : connection.Addons.FirstOrDefault(item => item.Name == addonName);
        if (addon is not null && File.Exists(Path.Combine(addon.GamePath, compiled.Replace('/', Path.DirectorySeparatorChar))))
        {
            return $"{AddonPrefix}{addon.Name}:{compiled}";
        }

        return connection.Archive.Contains(compiled) ? GamePrefix + compiled : null;
    }

    public static (AddonInfo Addon, string File)? ResolveAddon(GameConnection connection, string key)
    {
        if (!key.StartsWith(AddonPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = key[AddonPrefix.Length..];
        var colon = rest.IndexOf(':');
        var addon = colon < 0 ? null : connection.Addons.FirstOrDefault(item => item.Name == rest[..colon]);
        if (addon is null)
        {
            throw new FileNotFoundException($"The preview model's addon is not installed: {key}");
        }

        return (addon, IO.AtomicFile.SafeJoin(addon.GamePath, rest[(colon + 1)..]));
    }
}
