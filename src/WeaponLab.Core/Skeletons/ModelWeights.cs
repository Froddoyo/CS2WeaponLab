using SharpGLTF.Schema2;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Skeletons;

/// <summary>
/// Which of a model's bones carry mesh. A bone no vertex is weighted to is
/// often a leftover: ModelDoc builds a model's bones from every file the
/// .vmdl lists, so old animation files add their bones too (the Annabelle's
/// Nova-era DMX animations, 2026-10-29).
/// </summary>
public static class ModelWeights
{
    /// <summary>Bones that never carry mesh on a weapon: the root, the body anchor and attachment-like bones.</summary>
    public static readonly IReadOnlySet<string> Structural = new HashSet<string>(StringComparer.Ordinal) { "weapon", "weapon_offset", "root_motion", "muzzle", "econ", "flash" };

    /// <summary>The names of the bones some vertex is weighted to, or null when the model could not be exported.</summary>
    public static IReadOnlySet<string>? WeightedBones(GameConnection connection, string sourceModel)
    {
        var folder = Path.Combine(Path.GetTempPath(), "weaponlab-weights-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var glb = Path.Combine(folder, "model.glb");
            using (var exporter = new PreviewExporter(connection.Install))
            {
                if (PreviewModels.ResolveAddon(connection, sourceModel) is { } addon)
                {
                    exporter.ExportAddonModel(addon.File, addon.Addon.GamePath, glb, meshFilter: null);
                }
                else
                {
                    exporter.Export(sourceModel[PreviewModels.GamePrefix.Length..], glb, meshFilter: null);
                }
            }

            return Read(glb);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temporary folder is harmless.
            }
        }
    }

    private static HashSet<string> Read(string glb)
    {
        var weighted = new HashSet<string>(StringComparer.Ordinal);
        var model = ModelRoot.Load(glb);
        foreach (var node in model.LogicalNodes.Where(node => node.Skin is not null && node.Mesh is not null))
        {
            foreach (var primitive in node.Mesh!.Primitives)
            {
                var joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
                var weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
                for (var vertex = 0; joints is not null && weights is not null && vertex < joints.Count; vertex++)
                {
                    float[] w = [weights[vertex].X, weights[vertex].Y, weights[vertex].Z, weights[vertex].W];
                    float[] j = [joints[vertex].X, joints[vertex].Y, joints[vertex].Z, joints[vertex].W];
                    for (var slot = 0; slot < 4; slot++)
                    {
                        if (w[slot] > 0 && node.Skin!.GetJoint((int)j[slot]).Joint.Name is { } name)
                        {
                            weighted.Add(name);
                        }
                    }
                }
            }
        }

        return weighted;
    }
}
