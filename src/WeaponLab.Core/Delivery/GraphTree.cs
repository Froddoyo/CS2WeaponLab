using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Delivery;

/// <param name="Path">Graph resource path without <c>_c</c>, as graphs reference each other.</param>
/// <param name="Resources">The graph's <c>m_resources</c>: clips and child graphs it plays.</param>
public sealed record GraphInfo(string Path, IReadOnlyList<string> Resources, string Sha256);

/// <summary>Which graphs to clone and how every resource reference is redirected.</summary>
/// <param name="ResourceMap">Stock path → custom path for clips and graphs (without <c>_c</c>).</param>
/// <param name="Graphs">Stock graphs to clone, each written to its custom path.</param>
/// <param name="UnreferencedClips">Edited clips no graph plays; delivered but never used in game.</param>
public sealed record GraphPlan(
    IReadOnlyDictionary<string, string> ResourceMap,
    IReadOnlyList<GraphInfo> Graphs,
    IReadOnlyList<string> UnreferencedClips);

/// <summary>
/// The viewmodel animation graphs and CS2AL's selection of which to clone:
/// every graph that plays an edited clip, its child branches, and every
/// parent up to the root, keeping only what the root can reach. The root is
/// always cloned to <c>viewmodel_custom.vnmgraph</c>, which player models'
/// <c>hudmodel</c> graph entry must point at. Third-person clips (0.26) go the
/// same way through the worldmodel graphs, to <c>worldmodel_custom.vnmgraph</c>,
/// cloned only when a third-person clip is edited.
/// </summary>
public static class GraphTree
{
    public const string GraphRoot = "animation/graphs/viewmodel/";
    public const string Parent = GraphRoot + "viewmodel.vnmgraph";
    public const string CustomParent = GraphRoot + "viewmodel_custom.vnmgraph";
    public const string CustomClipRoot = "animation/anims/custom/";

    /// <summary>The third-person graphs (root -> family -> variation, like the viewmodel's; read from files 2026-11-14).</summary>
    public const string WorldGraphRoot = "animation/graphs/worldmodel/";
    public const string WorldParent = WorldGraphRoot + "worldmodel.vnmgraph";
    public const string WorldCustomParent = WorldGraphRoot + "worldmodel_custom.vnmgraph";

    /// <summary>Where a weapon's edited clip goes in the addon: <c>custom/&lt;weapon&gt;/</c>, third-person clips in its <c>world/</c> folder.</summary>
    public static string CustomClipPath(string weaponId, string stockClipPath)
    {
        var folder = Weapons.WeaponCatalog.IsWorldClip(stockClipPath) ? weaponId + "/world/" : weaponId + "/";
        return CustomClipRoot + folder + System.IO.Path.GetFileName(stockClipPath);
    }

    public static string CustomGraphPath(string stockGraphPath)
    {
        var index = stockGraphPath.IndexOf(".vnmgraph", StringComparison.Ordinal);
        return index < 0 ? throw new ArgumentException("Not a graph path: " + stockGraphPath)
            : stockGraphPath[..index] + "_custom" + stockGraphPath[index..];
    }

    /// <param name="includeWorld">Also the third-person (worldmodel) graphs; only delivery of third-person clips needs them.</param>
    public static Dictionary<string, GraphInfo> Load(GameArchive archive, bool includeWorld = false)
    {
        bool Wanted(string path) => path.EndsWith(".vnmgraph_c", StringComparison.Ordinal)
            && (path.StartsWith(GraphRoot, StringComparison.Ordinal) || (includeWorld && path.StartsWith(WorldGraphRoot, StringComparison.Ordinal)));
        var graphs = new Dictionary<string, GraphInfo>(StringComparer.Ordinal);
        foreach (var path in archive.AllPaths().Where(Wanted))
        {
            var bytes = archive.ReadBytes(path);
            using var resource = new Resource { FileName = path };
            resource.Read(new MemoryStream(bytes));
            var data = resource.DataBlock as BinaryKV3 ?? throw new InvalidDataException("Not a KV3 graph: " + path);
            var resources = data.Data.Root["m_resources"].Values.Select(value => value.ToString() ?? "").ToList();
            var name = path[..^2];
            graphs[name] = new GraphInfo(name, resources, GameArchive.Sha256(bytes));
        }

        return graphs;
    }

    /// <param name="clipMap">Stock clip path → custom clip path (without <c>_c</c>).</param>
    /// <param name="editedGraphs">Graphs with node edits; cloned like graphs that play edited clips.</param>
    public static GraphPlan Plan(IReadOnlyDictionary<string, GraphInfo> graphs, IReadOnlyDictionary<string, string> clipMap, IEnumerable<string>? editedGraphs = null)
    {
        if (!graphs.ContainsKey(Parent))
        {
            throw new InvalidDataException("The game has no " + Parent + ".");
        }

        bool PlaysEdited(GraphInfo graph) => graph.Resources.Any(clipMap.ContainsKey);
        var selected = graphs.Values.Where(PlaysEdited).Select(graph => graph.Path).ToHashSet(StringComparer.Ordinal);
        selected.UnionWith((editedGraphs ?? []).Where(graphs.ContainsKey));
        var playedClips = selected.SelectMany(name => graphs[name].Resources).Where(clipMap.ContainsKey).ToHashSet();

        // Child branches of selected graphs (e.g. inspects), even if their clips are stock.
        Grow(selected, name => name is Parent or WorldParent ? [] : graphs[name].Resources.Where(graphs.ContainsKey));
        // Parents that route to selected graphs, up to the root.
        Grow(selected, name => graphs.Values.Where(graph => graph.Resources.Contains(name)).Select(graph => graph.Path));

        // Keep only graphs a root can reach. The viewmodel root is always kept; the worldmodel root (0.26) only
        // when a third-person graph is selected, so first-person work never clones the 1853-node third-person root.
        var reachable = new HashSet<string>(StringComparer.Ordinal) { Parent };
        if (selected.Contains(WorldParent))
        {
            reachable.Add(WorldParent);
        }

        Grow(reachable, name => graphs[name].Resources.Where(selected.Contains));

        var map = new Dictionary<string, string>(clipMap, StringComparer.Ordinal);
        foreach (var name in reachable)
        {
            map[name] = CustomGraphPath(name);
        }

        return new GraphPlan(
            map,
            reachable.Order(StringComparer.Ordinal).Select(name => graphs[name]).ToList(),
            clipMap.Keys.Where(clip => !playedClips.Contains(clip)).Order(StringComparer.Ordinal).ToList());
    }

    private static void Grow(HashSet<string> set, Func<string, IEnumerable<string>> next)
    {
        var queue = new Queue<string>(set);
        while (queue.Count > 0)
        {
            foreach (var item in next(queue.Dequeue()))
            {
                if (set.Add(item))
                {
                    queue.Enqueue(item);
                }
            }
        }
    }
}
