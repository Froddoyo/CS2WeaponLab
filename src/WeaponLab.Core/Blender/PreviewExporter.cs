using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Blender;

/// <summary>
/// Exports untextured stock meshes as GLB so the Blender scene shows a real
/// weapon, sleeves and gloves. Previews are optional: failures become
/// warnings and the rigs still work without them.
/// </summary>
internal sealed class PreviewExporter : IDisposable
{
    public const string ArmsModel = "agents/models/ctm_sas/ctm_sas.vmdl_c";
    public const string GlovesModel = "agents/models/shared/arms/glove_hardknuckle/glove_hardknuckle.vmdl_c";

    private readonly Package _package = new();
    private readonly GameFileLoader _loader;

    // A private package instance: the exporter reads without our archive's lock.
    public PreviewExporter(GameInstall install)
    {
        _package.Read(install.CsgoVpkPath);
        _loader = new GameFileLoader(_package, install.CsgoVpkPath);
        _loader.AddDiskPathToSearch(install.CsgoPath);
        if (File.Exists(install.CoreVpkPath))
        {
            _loader.AddPackageToSearch(install.CoreVpkPath);
        }
    }

    /// <summary>
    /// Finds the weapon model that references <paramref name="weaponSkeleton"/>,
    /// preferring one in a folder named after the skeleton and skipping
    /// magazine and world models.
    /// </summary>
    public string? FindWeaponModel(string weaponSkeleton)
    {
        var stem = Path.GetFileNameWithoutExtension(weaponSkeleton);
        var candidates = _package.Entries?
            .SelectMany(group => group.Value)
            .Select(entry => entry.GetFullPath())
            .Where(path => path.StartsWith("weapons/models/", StringComparison.Ordinal) && path.EndsWith(".vmdl_c", StringComparison.Ordinal))
            .OrderBy(path => path.Contains("/" + stem + "/", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path.Contains("_mag.", StringComparison.Ordinal) || path.Contains("_world.", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(path => path.Length)
            .ToList() ?? [];

        foreach (var path in candidates)
        {
            using var resource = Load(path);
            // The skeleton references are in the model's base data, not its game key-values.
            if (resource?.DataBlock is Model { Data: { } data }
                && data.TryGetValue("m_vecNmSkeletonRefs", out var refs)
                && refs.Values.Any(value => value.ToString() == weaponSkeleton))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>An agent model's meshes as seen in third person (ctm_sas, read 2026-11-14): no first-person sleeves or arms.</summary>
    public static readonly string[] BodyMeshes = ["thirdperson_body", "thirdperson_default_gloves"];

    /// <param name="meshFilter">Mesh names to keep; null keeps all meshes.</param>
    public void Export(string modelPath, string outputPath, string? meshFilter)
    {
        Export(modelPath, outputPath, meshFilter is null ? [] : [meshFilter]);
    }

    /// <param name="meshes">The meshes to keep (those the model has); none of them, or an empty list, keeps all meshes.</param>
    public void Export(string modelPath, string outputPath, IReadOnlyCollection<string> meshes)
    {
        using var resource = Load(modelPath) ?? throw new FileNotFoundException($"Model not found: {modelPath}");
        Export(resource, outputPath, meshes);
    }

    /// <summary>
    /// Exports a compiled model from an addon folder; the addon is added to
    /// the search path so its meshes and materials resolve.
    /// </summary>
    public void ExportAddonModel(string file, string addonGamePath, string outputPath, string? meshFilter)
    {
        _loader.AddDiskPathToSearch(addonGamePath);
        using var resource = new Resource { FileName = Path.GetFileName(file) };
        resource.Read(file);
        Export(resource, outputPath, meshFilter is null ? [] : [meshFilter]);
    }

    /// <summary>Bone names of a model, to check it fits a weapon rig.</summary>
    public IReadOnlyList<string> BoneNames(string modelPath, string? addonFile)
    {
        using var resource = addonFile is null ? Load(modelPath) : ReadFile(addonFile);
        return resource?.DataBlock is Model model ? model.Skeleton.Bones.Select(bone => bone.Name).ToList() : [];
    }

    private static Resource ReadFile(string file)
    {
        var resource = new Resource { FileName = Path.GetFileName(file) };
        resource.Read(file);
        return resource;
    }

    private void Export(Resource resource, string outputPath, IReadOnlyCollection<string> meshes)
    {
        // Animations must stay on, or the GLB loses its skeleton; the filter matches no animation.
        var exporter = new GltfModelExporter(new NoAnimationLoader(_loader))
        {
            ProgressReporter = new Progress<string>(),
            ExportAnimations = true,
            ExportMaterials = false,
        };
        exporter.AnimationFilter.Add("__weaponlab_no_animation__");

        if (resource.DataBlock is Model model)
        {
            var names = model.GetEmbeddedMeshes().Select(mesh => mesh.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var name in meshes.Where(names.Contains))
            {
                exporter.MeshFilter.Add(name);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        exporter.Export(resource, outputPath, CancellationToken.None);
    }

    private Resource? Load(string path)
    {
        var entry = _package.FindEntry(path);
        if (entry is null)
        {
            return null;
        }

        _package.ReadEntry(entry, out byte[] bytes);
        var resource = new Resource { FileName = path };
        resource.Read(new MemoryStream(bytes));
        return resource;
    }

    public void Dispose()
    {
        _loader.Dispose();
        _package.Dispose();
    }

    /// <summary>Hides animation resources so the GLB carries only meshes and bind pose.</summary>
    private sealed class NoAnimationLoader(IFileLoader inner) : IFileLoader
    {
        private static bool IsAnimation(string path) =>
            path.StartsWith("animation/", StringComparison.Ordinal)
            || path.EndsWith(".vagrp", StringComparison.Ordinal) || path.EndsWith(".vagrp_c", StringComparison.Ordinal)
            || path.EndsWith(".vanim", StringComparison.Ordinal) || path.EndsWith(".vanim_c", StringComparison.Ordinal);

        public Resource? LoadFile(string path) => IsAnimation(path) ? null : inner.LoadFile(path);
        public Resource? LoadFileCompiled(string path) => IsAnimation(path) ? null : inner.LoadFileCompiled(path);
        public ValveResourceFormat.CompiledShader.ShaderCollection? LoadShader(string path) => inner.LoadShader(path);
        public Stream? GetFileStream(string path) => inner.GetFileStream(path);
    }
}
