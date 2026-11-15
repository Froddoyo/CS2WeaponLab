using System.Globalization;

namespace WeaponLab.Core.Graphs;

/// <summary>
/// Short plain-language summaries of nodes, e.g. a transition condition as
/// "action is action_reload and not weapon_is_using_ironsights".
/// </summary>
public static class GraphExplain
{
    private const int MaxDepth = 4;

    /// <summary>A one-line summary for a node box, or "" when the name says it all.</summary>
    public static string Subtitle(GraphDocument graph, GraphNode node, IReadOnlyDictionary<string, string>? edits = null)
    {
        string Value(string path) => edits?.GetValueOrDefault(GraphEdits.Key(node.Index, path)) ?? node.Value(path) ?? "";
        return node.Class switch
        {
            "ClipNode" or "AnimationPoseNode" => PlayedName(graph, node, edits),
            "TransitionNode" => $"blend {Value("m_flDuration")} s → {TargetName(graph, node)}",
            "StateMachineNode" => $"{graph.Transitions.GetValueOrDefault(node.Index)?.Select(transition => transition.From).Distinct().Count() ?? 0} states, starts in {DefaultStateName(graph, node)}",
            "StateNode" => node.Links.FirstOrDefault(link => link.Field == "m_nChildNodeIdx") is { } child && child.Target < graph.Nodes.Count
                ? "plays " + GraphGlossary.Describe(graph[child.Target].Class).Name.ToLowerInvariant()
                : "",
            "ControlParameterFloatNode" or "ControlParameterIDNode" or "ControlParameterBoolNode" => "from the game",
            "ConstFloatNode" => Value("m_flValue"),
            "ConstIDNode" => Value("m_value"),
            "BoneMaskNode" => Value("m_boneMaskID"),
            "ReferencedGraphNode" => graph.PlayedResource(node) is { } child ? "plays " + Path.GetFileName(child) : "another graph",
            _ when GraphGlossary.Describe(node.Class).Category == GraphNodeCategory.Logic => Condition(graph, node.Index, edits),
            _ => "",
        };
    }

    public static string PlayedName(GraphDocument graph, GraphNode node, IReadOnlyDictionary<string, string>? edits = null)
    {
        var played = edits?.GetValueOrDefault(GraphEdits.Key(node.Index, GraphDocument.ClipEditKey)) ?? graph.PlayedResource(node);
        return played is null ? "(no clip in this variation)" : Path.GetFileNameWithoutExtension(played);
    }

    public static string TargetName(GraphDocument graph, GraphNode transition) =>
        transition.Links.FirstOrDefault(link => link.Field == "m_nTargetStateNodeIdx") is { } target && target.Target < graph.Nodes.Count
            ? graph[target.Target].Name
            : "?";

    public static string DefaultStateName(GraphDocument graph, GraphNode machine) =>
        graph.DefaultStates.TryGetValue(machine.Index, out var state) ? graph[state].Name : "?";

    /// <summary>A readable condition: "action is action_reload and weapon_ammo ≤ 0".</summary>
    public static string Condition(GraphDocument graph, int index, IReadOnlyDictionary<string, string>? edits = null, int depth = 0)
    {
        if (index < 0 || index >= graph.Nodes.Count)
        {
            return "always";
        }

        var node = graph[index];
        string Value(string path) => edits?.GetValueOrDefault(GraphEdits.Key(node.Index, path)) ?? node.Value(path) ?? "";
        string Input(string field) => node.Links.FirstOrDefault(link => link.Field == field) is { } link ? Condition(graph, link.Target, edits, depth + 1) : "?";
        List<string> Many(string field) => node.Links.Where(link => link.Field.StartsWith(field + "[", StringComparison.Ordinal))
            .Select(link => Condition(graph, link.Target, edits, depth + 1)).ToList();
        List<string> Strings(string field) => node.Values.Where(value => value.Path.StartsWith(field + "[", StringComparison.Ordinal))
            .Select(value => edits?.GetValueOrDefault(GraphEdits.Key(node.Index, value.Path)) ?? value.Value).Where(text => text.Length > 0).ToList();

        if (depth > MaxDepth)
        {
            return "…";
        }

        // Named values (game values, and virtual parameters like "weapon_is_using_ironsights/And") read as their name.
        if (node.Class.StartsWith("ControlParameter", StringComparison.Ordinal))
        {
            return node.Name;
        }

        if (depth > 0 && node.Path.Contains('/') && graph.ControlParameters.Count > 0 && IsVirtualParameter(graph, node))
        {
            return node.Path[..node.Path.IndexOf('/')];
        }

        return node.Class switch
        {
            "AndNode" => Join(Many("m_conditionNodeIndices"), " and "),
            "OrNode" => Join(Many("m_conditionNodeIndices"), " or "),
            "NotNode" => Negate(Input("m_nInputValueNodeIdx")),
            "IDComparisonNode" => $"{Input("m_nInputValueNodeIdx")} {(Value("m_comparison") == "DoesntMatch" ? "is not" : "is")} {string.Join(" / ", Strings("m_comparisionIDs"))}",
            "FloatComparisonNode" => $"{Input("m_nInputValueNodeIdx")} {Operator(Value("m_comparison"))} {(node.Links.Any(link => link.Field == "m_nComparandValueNodeIdx") ? Input("m_nComparandValueNodeIdx") : Value("m_flComparisonValue"))}",
            "FloatMathNode" => $"({Input("m_nInputValueNodeIdxA")} {MathOperator(Value("m_operator"))} {(node.Links.Any(link => link.Field == "m_nInputValueNodeIdxB") ? Input("m_nInputValueNodeIdxB") : Value("m_flValueB"))})",
            "GraphEventConditionNode" => "event " + string.Join(" / ", node.Values.Where(value => value.Path.EndsWith(".m_eventID", StringComparison.Ordinal)).Select(value => value.Value)),
            "IDEventConditionNode" => "event " + string.Join(" / ", Strings("m_eventIDs")),
            "StateCompletedConditionNode" => "the state has finished",
            "ConstFloatNode" => Value("m_flValue"),
            "ConstIDNode" => Value("m_value"),
            "CurrentSyncEventIDNode" => "current sync event",
            "FloatSwitchNode" or "IDSwitchNode" => $"(if {Input("m_nSwitchValueNodeIdx")})",
            _ => node.Name,
        };
    }

    // "action is x" → "action is not x"; anything else gets "not".
    private static string Negate(string condition) =>
        condition.Contains(" is not ", StringComparison.Ordinal) ? condition.Replace(" is not ", " is ", StringComparison.Ordinal)
        : !condition.Contains(" and ", StringComparison.Ordinal) && !condition.Contains(" or ", StringComparison.Ordinal) && condition.Contains(" is ", StringComparison.Ordinal) ? condition.Replace(" is ", " is not ", StringComparison.Ordinal)
        : condition.Contains(' ') ? "not (" + condition + ")" : "not " + condition;

    private static bool IsVirtualParameter(GraphDocument graph, GraphNode node)
    {
        var top = node.Path[..node.Path.IndexOf('/')];
        return graph.Nodes.All(other => other.Path != top) && !top.Contains(' ') && node.Path.Count(character => character == '/') == 1;
    }

    private static string Join(List<string> parts, string separator) =>
        parts.Count switch
        {
            0 => "always",
            1 => parts[0],
            _ => string.Join(separator, parts.Select(part => part.Contains(" and ") || part.Contains(" or ") ? "(" + part + ")" : part)),
        };

    private static string Operator(string comparison) => comparison switch
    {
        "GreaterThanEqual" => "≥",
        "LessThanEqual" => "≤",
        "GreaterThan" => ">",
        "LessThan" => "<",
        "NearEqual" => "≈",
        _ => comparison,
    };

    private static string MathOperator(string name) => name switch
    {
        "Add" => "+",
        "Sub" => "−",
        "Mul" => "×",
        "Div" => "÷",
        "Mod" => "mod",
        _ => name,
    };

    /// <summary>"Idle → Reload when action is action_reload (blend 0.1 s)".</summary>
    public static string Transition(GraphDocument graph, GraphTransition transition, IReadOnlyDictionary<string, string>? edits = null)
    {
        var duration = edits?.GetValueOrDefault(GraphEdits.Key(transition.TransitionNode, "m_flDuration")) ?? graph[transition.TransitionNode].Value("m_flDuration") ?? "0";
        var seconds = double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value.ToString("0.###", CultureInfo.InvariantCulture) : duration;
        return $"{graph[transition.From].Name} → {graph[transition.To].Name} when {Condition(graph, transition.ConditionNode, edits)} (blend {seconds} s)";
    }
}
