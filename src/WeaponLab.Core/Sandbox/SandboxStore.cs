using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Sandbox;

/// <summary>An opened sandbox: its folder and manifest.</summary>
public sealed record SandboxInfo(string FolderPath, SandboxManifest Manifest);

/// <summary>
/// Creates and lists weapon sandboxes in a workspace folder. The workspace
/// holds game-derived data and must live outside the repository.
/// </summary>
public sealed class SandboxStore
{
    public const string TemplatesFolder = "templates";

    public SandboxStore(string workspacePath)
        : this(workspacePath, Path.Combine(workspacePath, "sandboxes"))
    {
    }

    /// <param name="sandboxesPath">Where the sandboxes live: a profile's own folder (see <see cref="Profiles.ProfileStore.SandboxesFor"/>).</param>
    public SandboxStore(string workspacePath, string sandboxesPath)
    {
        WorkspacePath = Path.GetFullPath(workspacePath);
        SandboxesPath = Path.GetFullPath(sandboxesPath);
    }

    /// <summary>The workspace: deliveries, backups and addon originals are kept here for every profile.</summary>
    public string WorkspacePath { get; }

    public string SandboxesPath { get; }
    private string StagingPath => Path.Combine(Path.GetDirectoryName(SandboxesPath)!, ".staging");

    public const string WorkspaceFolderName = "workspace";
    private const string RepositoryMarker = "CS2WeaponLab.slnx";

    /// <summary>
    /// The workspace, first match wins (0.24): the <c>WEAPONLAB_WORKSPACE</c> environment variable; the folder
    /// picked in Settings (<see cref="Settings.AppSettings.WorkspaceFolder"/>); with no settings saved yet, a
    /// workspace earlier versions used that holds profiles (the repository's gitignored <c>workspace</c> folder,
    /// else <c>%LOCALAPPDATA%\CS2WeaponLab\workspace</c>), so updating loses nothing; else the default,
    /// <c>workspace</c> beside the application (user decision 2026-11-09).
    /// </summary>
    public static string DefaultWorkspacePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("WEAPONLAB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        if (Settings.AppSettings.Load().WorkspaceFolder is { Length: > 0 } chosen)
        {
            return chosen;
        }

        if (!Settings.AppSettings.Exists && EarlierWorkspace() is { } earlier)
        {
            return earlier;
        }

        return BesideApplication();
    }

    /// <summary>The default workspace: <c>workspace</c> in the application's own folder.</summary>
    public static string BesideApplication() => Path.Combine(AppContext.BaseDirectory, WorkspaceFolderName);

    /// <summary>The workspace versions before 0.24 used, when it holds profiles; null otherwise.</summary>
    public static string? EarlierWorkspace()
    {
        var repository = FindRepositoryRoot(AppContext.BaseDirectory);
        var candidates = new[]
        {
            repository is null ? null : Path.Combine(repository, WorkspaceFolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CS2WeaponLab", WorkspaceFolderName),
        };
        return candidates.FirstOrDefault(folder => folder is not null && Directory.Exists(Path.Combine(folder, "profiles")));
    }

    /// <summary>
    /// Walks up from <paramref name="startFolder"/> to the folder holding the
    /// solution file. Returns null for builds running outside the repository.
    /// </summary>
    internal static string? FindRepositoryRoot(string startFolder)
    {
        for (var folder = new DirectoryInfo(startFolder); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, RepositoryMarker)))
            {
                return folder.FullName;
            }
        }

        return null;
    }

    public string FolderFor(string weaponId) => AtomicFile.SafeJoin(SandboxesPath, weaponId);

    public bool Exists(string weaponId) => File.Exists(Path.Combine(FolderFor(weaponId), SandboxManifest.FileName));

    public SandboxInfo Open(string weaponId)
    {
        var folder = FolderFor(weaponId);
        var manifestPath = Path.Combine(folder, SandboxManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"No sandbox for '{weaponId}' in {SandboxesPath}.", manifestPath);
        }

        return new SandboxInfo(folder, SandboxManifest.FromJson(File.ReadAllText(manifestPath)));
    }

    /// <summary>Opens a sandbox by its folder, wherever it lives.</summary>
    public static SandboxInfo OpenFolder(string folder)
    {
        var full = Path.GetFullPath(folder);
        var manifestPath = Path.Combine(full, SandboxManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"{full} is not a Weapon Lab sandbox (no {SandboxManifest.FileName}).", manifestPath);
        }

        return new SandboxInfo(full, SandboxManifest.FromJson(File.ReadAllText(manifestPath)));
    }

    public IReadOnlyList<SandboxInfo> List()
    {
        if (!Directory.Exists(SandboxesPath))
        {
            return [];
        }

        return Directory.EnumerateDirectories(SandboxesPath)
            .Where(folder => File.Exists(Path.Combine(folder, SandboxManifest.FileName)))
            .Select(folder => Open(Path.GetFileName(folder)))
            .OrderBy(sandbox => sandbox.Manifest.WeaponId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Copies every clip of <paramref name="weapon"/> into a new sandbox and
    /// writes its manifest. Refuses to replace an existing sandbox.
    /// </summary>
    /// <param name="sourceAddon">Take each clip from this addon when it has its own version of it (see <see cref="AddonClips"/>); null = the game's clips.</param>
    public SandboxInfo Create(GameConnection connection, WeaponEntry weapon, string? targetAddon, IProgress<string>? progress = null, Delivery.AddonTarget? sourceAddon = null)
    {
        if (targetAddon is not null && connection.Addons.All(addon => addon.Name != targetAddon))
        {
            throw new ArgumentException($"Addon '{targetAddon}' does not exist in {connection.Install.GameAddonsPath}.");
        }

        var finalFolder = FolderFor(weapon.Id);
        if (Directory.Exists(finalFolder))
        {
            throw new IOException($"A sandbox for '{weapon.Id}' already exists at {finalFolder}.");
        }

        EnsureWorkspace();

        // Build in staging and move into place, so a failure leaves nothing half-made.
        var staging = Path.Combine(StagingPath, weapon.Id + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var manifest = Build(staging, connection, weapon, targetAddon, sourceAddon, progress);
            Directory.CreateDirectory(SandboxesPath);
            Directory.Move(staging, finalFolder);
            return new SandboxInfo(finalFolder, manifest);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>
    /// Takes the sandbox's templates again, from the game or from an addon.
    /// Exports, the .blend and its location are kept; a custom skeleton is
    /// dropped (its templates were the old ones), so build the .blend again.
    /// </summary>
    public SandboxInfo Rebuild(SandboxInfo sandbox, GameConnection connection, WeaponEntry weapon, string? targetAddon, Delivery.AddonTarget? sourceAddon, IProgress<string>? progress = null)
    {
        EnsureWorkspace();
        var staging = Path.Combine(StagingPath, weapon.Id + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var manifest = Build(staging, connection, weapon, targetAddon, sourceAddon, progress);
            foreach (var old in new[] { TemplatesFolder, Skeletons.CustomSkeletons.Folder })
            {
                var folder = Path.Combine(sandbox.FolderPath, old);
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }

            Directory.Move(Path.Combine(staging, TemplatesFolder), Path.Combine(sandbox.FolderPath, TemplatesFolder));
            AtomicFile.WriteAllText(Path.Combine(sandbox.FolderPath, SandboxManifest.FileName), manifest.ToJson());
            return new SandboxInfo(sandbox.FolderPath, manifest);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>
    /// The addon's own version of each of the weapon's game clips: the clip
    /// its graphs play instead, or a clip that stands in for it by name
    /// (game clip path → addon clip path, without <c>_c</c>). Empty when the
    /// addon has none.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AddonClips(GameConnection connection, WeaponEntry weapon, Delivery.AddonTarget addon)
    {
        var inventory = Addons.AddonInventory.Load(connection, addon, Delivery.AddonManifest.Read(addon));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stock in weapon.ClipPaths.Concat(weapon.WorldClipPaths).Select(path => path[..^2]))
        {
            var own = inventory.ClipOverrides.GetValueOrDefault(stock)
                ?? inventory.Assets.FirstOrDefault(asset => asset.Kind == Addons.AddonAssetKind.Clip && asset.Replaces == stock)?.Path;
            if (own is not null && File.Exists(AtomicFile.SafeJoin(addon.GamePath, own + "_c")))
            {
                result[stock] = own;
            }
        }

        return result;
    }

    /// <summary>Writes the templates and sandbox.json of a sandbox into <paramref name="folder"/>.</summary>
    private static SandboxManifest Build(string folder, GameConnection connection, WeaponEntry weapon, string? targetAddon, Delivery.AddonTarget? sourceAddon, IProgress<string>? progress)
    {
        var addonClips = sourceAddon is null ? new Dictionary<string, string>() : AddonClips(connection, weapon, sourceAddon);
        var clips = Templates(folder, connection, weapon.ClipPaths, addonClips, sourceAddon, progress);
        var worldClips = Templates(folder, connection, weapon.WorldClipPaths, addonClips, sourceAddon, progress);

        var version = connection.Version;
        var manifest = new SandboxManifest
        {
            WeaponId = weapon.Id,
            Category = weapon.Category,
            VpkFolder = weapon.Folder,
            TargetAddon = targetAddon,
            SourceAddon = addonClips.Count > 0 ? sourceAddon!.Name : null,
            CreatedUtc = DateTimeOffset.UtcNow,
            Game = new SandboxGameBuild(
                version?.PatchVersion ?? "unknown",
                version?.ClientVersion ?? "unknown",
                version?.SourceRevision ?? "unknown"),
            Clips = clips,
            WorldClips = worldClips,
        };
        AtomicFile.WriteAllText(Path.Combine(folder, SandboxManifest.FileName), manifest.ToJson());
        return manifest;
    }

    /// <summary>Copies each clip (CS2's, or the addon's own version) to <c>templates/&lt;resource path&gt;</c> in <paramref name="folder"/>.</summary>
    private static List<SandboxClip> Templates(string folder, GameConnection connection, IEnumerable<string> resourcePaths,
        IReadOnlyDictionary<string, string> addonClips, Delivery.AddonTarget? sourceAddon, IProgress<string>? progress)
    {
        var clips = new List<SandboxClip>();
        foreach (var resourcePath in resourcePaths)
        {
            progress?.Report($"Reading {resourcePath[(resourcePath.LastIndexOf('/') + 1)..]}");
            var addonClip = addonClips.GetValueOrDefault(resourcePath[..^2]);
            var bytes = addonClip is null
                ? connection.Archive.ReadBytes(resourcePath)
                : File.ReadAllBytes(AtomicFile.SafeJoin(sourceAddon!.GamePath, addonClip + "_c"));
            var clip = ClipReader.Read(bytes, resourcePath);

            var template = $"{TemplatesFolder}/{resourcePath}";
            AtomicFile.WriteAllBytes(AtomicFile.SafeJoin(folder, template), bytes);
            var editable = Weapons.WeaponCatalog.IsWorldClip(resourcePath) ? clip.IsEditableWorld : clip.IsEditable;
            clips.Add(new SandboxClip(resourcePath, template, clip.Sha256, clip.FrameCount, clip.DurationSeconds, editable, addonClip));
        }

        return clips;
    }

    /// <summary>
    /// Adds the weapon's third-person clips (CS2's) to a sandbox made before 0.26; nothing else in the sandbox
    /// changes. Build the .blend again ("Keep my animation") to get them in Blender. Returns the sandbox unchanged
    /// when it already has them or the game has none for this weapon.
    /// </summary>
    public static SandboxInfo AddWorldClips(SandboxInfo sandbox, GameConnection connection, WeaponEntry weapon, IProgress<string>? progress = null)
    {
        if (sandbox.Manifest.WorldClips.Count > 0 || weapon.WorldClipPaths.Count == 0)
        {
            return sandbox;
        }

        var worldClips = Templates(sandbox.FolderPath, connection, weapon.WorldClipPaths, new Dictionary<string, string>(), null, progress);
        var manifest = sandbox.Manifest with { WorldClips = worldClips };
        AtomicFile.WriteAllText(Path.Combine(sandbox.FolderPath, SandboxManifest.FileName), manifest.ToJson());
        return new SandboxInfo(sandbox.FolderPath, manifest);
    }
    /// <summary>
    /// Compares the sandbox's template fingerprints with the clips in the
    /// current game. Returns the resource paths that changed or disappeared.
    /// </summary>
    public static IReadOnlyList<string> ChangedSinceImport(SandboxInfo sandbox, GameArchive archive)
    {
        var changed = new List<string>();
        foreach (var clip in sandbox.Manifest.AllClips())
        {
            if (clip.AddonClip is not null)
            {
                continue; // Taken from the addon, not the game.
            }

            if (!archive.Contains(clip.ResourcePath) || GameArchive.Sha256(archive.ReadBytes(clip.ResourcePath)) != clip.Sha256)
            {
                changed.Add(clip.ResourcePath);
            }
        }

        return changed;
    }

    private void EnsureWorkspace()
    {
        Directory.CreateDirectory(WorkspacePath);
        var ignore = Path.Combine(WorkspacePath, ".gitignore");
        if (!File.Exists(ignore))
        {
            File.WriteAllText(ignore, "# CS2-derived workspace data; never commit it.\n*\n");
        }
    }
}
