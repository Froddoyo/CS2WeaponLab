using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Graphs;

/// <summary>Whether one graph's profile edits are in the target addon.</summary>
/// <param name="Owners">Weapons whose clips the graph plays.</param>
/// <param name="Pending">Edit keys the addon's custom graph does not hold yet.</param>
public sealed record GraphEditState(string Graph, IReadOnlyList<string> Owners, IReadOnlyDictionary<string, string> Edits, IReadOnlySet<string> Pending)
{
    public bool IsApplied => Pending.Count == 0;

    /// <summary>Node index → true when all its edits are in the addon.</summary>
    public IReadOnlyDictionary<int, bool> Nodes =>
        Edits.Keys.GroupBy(key => GraphEdits.Parse(key).Node).ToDictionary(group => group.Key, group => group.All(key => !Pending.Contains(key)));
}

/// <summary>
/// Compares the profile's graph edits with the custom graphs delivered to
/// the target addon (<c>*_custom.vnmgraph_c</c>), for the outliner and the
/// graph editor's colours.
/// </summary>
public static class GraphStatus
{
    public static Dictionary<string, GraphEditState> Load(Profile profile, GameConnection connection, AddonTarget? target)
    {
        var result = new Dictionary<string, GraphEditState>(StringComparer.Ordinal);
        foreach (var (graph, edits) in profile.Graphs.Where(item => item.Value.Count > 0))
        {
            var stockPath = graph + "_c";
            if (!connection.Archive.Contains(stockPath))
            {
                continue;
            }

            var stock = GraphDocument.Read(connection.Archive.ReadBytes(stockPath), stockPath);
            var owners = DeliveryPlanner.GraphOwners(connection, new GraphInfo(graph, stock.Resources, ""));
            var delivered = ReadDelivered(target, graph);
            var pending = edits.Where(edit => delivered is null || !Holds(delivered, edit.Key, edit.Value)).Select(edit => edit.Key).ToHashSet(StringComparer.Ordinal);
            result[graph] = new GraphEditState(graph, owners, edits, pending);
        }

        return result;
    }

    /// <summary>The addon's live custom copy of a stock graph (whoever made it), or null. The Graph tab shows it and pushes build on it.</summary>
    public static GraphDocument? ReadDelivered(AddonTarget? target, string stockGraph)
    {
        if (target is null)
        {
            return null;
        }

        var custom = GraphTree.CustomGraphPath(stockGraph) + "_c";
        var path = Path.Combine(target.GamePath, custom.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            return File.Exists(path) ? GraphDocument.Read(File.ReadAllBytes(path), custom) : null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }


    public static bool Holds(GraphDocument delivered, string key, string value)
    {
        var (node, path) = GraphEdits.Parse(key);
        if (node < 0 || node >= delivered.Nodes.Count)
        {
            return false;
        }

        if (path == GraphDocument.ClipEditKey)
        {
            return delivered.PlayedResource(delivered[node]) is { } played && SameClip(played, value);
        }

        return delivered[node].Value(path) is { } actual && VDataValues.Same(actual, value);
    }

    /// <summary>
    /// A node playing <paramref name="played"/> plays <paramref name="clip"/>: the same path, or the
    /// addon's copy of it (edited clips are delivered under animation/anims/custom/&lt;weapon&gt;/ with the same file name).
    /// </summary>
    public static bool SameClip(string played, string clip) =>
        played == clip
        || (played.StartsWith(GraphTree.CustomClipRoot, StringComparison.Ordinal) && Path.GetFileName(played) == Path.GetFileName(clip));
}
