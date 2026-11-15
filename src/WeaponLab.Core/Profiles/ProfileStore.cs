using System.Text.Json;
using WeaponLab.Core.IO;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Profiles;

/// <summary>
/// Profiles are local workspaces (user decision 2026-10-26): each lives in
/// <c>workspace/profiles/&lt;name&gt;/</c> with its <c>profile.json</c> (settings and
/// changes not pushed yet) and its own <c>sandboxes/</c> (Blender files and
/// exports). The active one is named in <c>workspace/profiles/active.txt</c>.
/// A profile is not tied to an addon: the addon is the live state, and
/// applying pushes the profile's pending changes into whichever addon is picked.
/// </summary>
public sealed class ProfileStore
{
    public const string FileName = "profile.json";
    public const string DefaultName = "Default";
    private const string ActiveFile = "active.txt";

    /// <summary>Profiles before 0.15 were single files named after their addon.</summary>
    private const string LegacyExtension = ".wlprofile.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ProfileStore(string workspacePath)
    {
        WorkspacePath = Path.GetFullPath(workspacePath);
    }

    public string WorkspacePath { get; }
    public string Folder => Path.Combine(WorkspacePath, "profiles");

    public string FolderFor(string name) => AtomicFile.SafeJoin(Folder, CheckName(name));

    public string PathFor(string name) => Path.Combine(FolderFor(name), FileName);

    /// <summary>Where a profile keeps its sandboxes (Blender files, exports).</summary>
    public string SandboxesPathFor(string name) => Path.Combine(FolderFor(name), "sandboxes");

    /// <summary>A sandbox store for one profile; deliveries and backups stay workspace-wide.</summary>
    public Sandbox.SandboxStore SandboxesFor(string name) => new(WorkspacePath, SandboxesPathFor(name));

    public IReadOnlyList<string> Names()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        return Directory.EnumerateDirectories(Folder)
            .Where(folder => File.Exists(Path.Combine(folder, FileName)))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? ActiveName =>
        File.Exists(Path.Combine(Folder, ActiveFile)) ? File.ReadAllText(Path.Combine(Folder, ActiveFile)).Trim() : null;

    /// <summary>The active profile, after moving older layouts over; creates "Default" on first use.</summary>
    public Profile LoadActive()
    {
        MigrateLegacy();
        if (ActiveName is { } active && Exists(active))
        {
            return Load(active);
        }

        var first = Names().FirstOrDefault();
        if (first is not null)
        {
            SetActive(first);
            return Load(first);
        }

        var profile = new Profile { Name = DefaultName };
        MigrateOldOverrides(profile);
        Save(profile);
        SetActive(profile.Name);
        return profile;
    }

    /// <summary>A new, empty profile (no sandboxes, no changes), made active.</summary>
    public Profile Create(string name, string? targetAddon)
    {
        if (Exists(name))
        {
            throw new ArgumentException($"A profile named '{name}' already exists.");
        }

        var profile = new Profile { Name = name.Trim(), TargetAddon = targetAddon };
        Save(profile);
        SetActive(profile.Name);
        return profile;
    }

    /// <summary>Renames a profile's folder and the name inside it.</summary>
    public Profile Rename(string name, string newName)
    {
        if (Exists(newName))
        {
            throw new ArgumentException($"A profile named '{newName}' already exists.");
        }

        var profile = Load(name);
        Directory.Move(FolderFor(name), FolderFor(newName));
        profile.Name = newName.Trim();
        Save(profile);
        if (ActiveName == name)
        {
            SetActive(profile.Name);
        }

        return profile;
    }

    /// <summary>
    /// Removes a profile: its folder (Blender files and exports included) is
    /// moved to <c>workspace/backups/profiles</c>, not deleted. Returns where.
    /// </summary>
    public string Delete(string name)
    {
        var backup = Path.Combine(WorkspacePath, "backups", "profiles", $"{CheckName(name)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        Directory.Move(FolderFor(name), backup);
        return backup;
    }

    public Profile Load(string name)
    {
        var profile = Parse(File.ReadAllText(PathFor(name)));
        profile.Name = name; // The folder is the name.
        return profile;
    }

    public static Profile Parse(string json)
    {
        var profile = JsonSerializer.Deserialize<Profile>(json, Json) ?? throw new InvalidDataException("The profile file is empty.");
        if (profile.Schema != Profile.CurrentSchema)
        {
            throw new InvalidDataException($"Unsupported profile format '{profile.Schema}'.");
        }

        return profile;
    }

    public void Save(Profile profile)
    {
        AtomicFile.WriteAllText(PathFor(profile.Name), ToJson(profile));
    }

    public static string ToJson(Profile profile) => JsonSerializer.Serialize(profile, Json) + "\n";

    public void SetActive(string name)
    {
        Directory.CreateDirectory(Folder);
        AtomicFile.WriteAllText(Path.Combine(Folder, ActiveFile), CheckName(name));
    }

    public bool Exists(string name) => File.Exists(PathFor(name));

    private static string CheckName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed is "." or "..")
        {
            throw new ArgumentException($"'{name}' cannot be used as a profile name.");
        }

        return trimmed;
    }

    /// <summary>
    /// Moves the 0.13–0.14 layout over: each <c>profiles/&lt;name&gt;.wlprofile.json</c>
    /// becomes <c>profiles/&lt;name&gt;/profile.json</c>, and the shared
    /// <c>workspace/sandboxes</c> goes into the active profile (they were
    /// shared by every addon, so that is where they were being used).
    /// Returns what was moved, for the log.
    /// </summary>
    public IReadOnlyList<string> MigrateLegacy()
    {
        var moved = new List<string>();
        if (Directory.Exists(Folder))
        {
            foreach (var file in Directory.EnumerateFiles(Folder, "*" + LegacyExtension))
            {
                var name = Path.GetFileName(file)[..^LegacyExtension.Length];
                if (Exists(name))
                {
                    continue;
                }

                Directory.CreateDirectory(FolderFor(name));
                File.Move(file, PathFor(name));
                moved.Add($"profile {name}");
            }
        }

        var shared = Path.Combine(WorkspacePath, "sandboxes");
        if (Directory.Exists(shared))
        {
            var owner = ActiveName is { } active && Exists(active) ? active : Names().FirstOrDefault() ?? DefaultName;
            if (!Exists(owner))
            {
                Save(new Profile { Name = owner });
            }

            var destination = SandboxesPathFor(owner);
            if (!Directory.Exists(destination))
            {
                Directory.Move(shared, destination);
                moved.Add($"sandboxes into profile {owner}");
            }
        }

        return moved;
    }

    // Weapon Lab 0.6–0.7 kept vdata edits in workspace/vdata/overrides.json.
    private void MigrateOldOverrides(Profile profile)
    {
        var old = VDataOverrides.PathFor(WorkspacePath);
        if (!File.Exists(old))
        {
            return;
        }

        foreach (var (block, path, value) in VDataOverrides.Load(WorkspacePath).All())
        {
            if (!profile.VData.TryGetValue(block, out var values))
            {
                profile.VData[block] = values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            }

            values[path] = value;
        }
    }
}
