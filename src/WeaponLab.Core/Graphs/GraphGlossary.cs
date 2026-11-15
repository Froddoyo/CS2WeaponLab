using ValveKeyValue;

namespace WeaponLab.Core.Graphs;

/// <summary>How the editor draws and groups a node.</summary>
public enum GraphNodeCategory
{
    /// <summary>Values the game sends in every frame: action, weapon_type, ammo…</summary>
    Parameter,

    /// <summary>Picks one state at a time and moves between them.</summary>
    StateMachine,

    State,

    Transition,

    /// <summary>Plays an animation clip or holds a pose.</summary>
    Clip,

    /// <summary>Chooses or mixes animations: selectors, blends, layers.</summary>
    Blend,

    /// <summary>True/false and number logic that drives choices and transitions.</summary>
    Logic,

    /// <summary>Constant values.</summary>
    Value,

    /// <summary>Another graph played inside this one.</summary>
    Graph,

    /// <summary>Pose fix-ups such as scaling and IK.</summary>
    Pose,
}

public sealed record GraphNodeTerm(GraphNodeCategory Category, string Name, string Description);

public sealed record GraphFieldTerm(string Name, string Description);

/// <summary>
/// Plain-language names for CS2's animation graph node types and their
/// fields, which values may be edited, and the choices for enum fields.
/// Written from the compiled viewmodel graphs (2026-10-24); behaviour notes
/// are best understanding, not Valve documentation.
/// </summary>
public static class GraphGlossary
{
    private static readonly Dictionary<string, GraphNodeTerm> Nodes = new(StringComparer.Ordinal)
    {
        ["ControlParameterFloatNode"] = new(GraphNodeCategory.Parameter, "Game value (number)", "A number the game sends to the graph every frame, e.g. ammo in the magazine or ironsight amount."),
        ["ControlParameterIDNode"] = new(GraphNodeCategory.Parameter, "Game value (name)", "A name the game sends every frame, e.g. the current action (action_reload) or the weapon (weapon_ak47)."),
        ["ControlParameterBoolNode"] = new(GraphNodeCategory.Parameter, "Game value (yes/no)", "A true/false flag the game sends every frame, e.g. whether the silencer is on."),
        ["StateMachineNode"] = new(GraphNodeCategory.StateMachine, "State machine", "Plays exactly one of its states at a time and switches to another when a transition's condition becomes true. Open it to see its states."),
        ["StateNode"] = new(GraphNodeCategory.State, "State", "One situation the weapon can be in (idle, reload, attack…). Open it to see what it plays. States can also fire events while active."),
        ["TransitionNode"] = new(GraphNodeCategory.Transition, "Transition", "How a state machine blends into its target state: the blend time, easing and offsets."),
        ["ClipNode"] = new(GraphNodeCategory.Clip, "Clip", "Plays one animation clip. Which clip comes from this weapon's variation of the graph."),
        ["AnimationPoseNode"] = new(GraphNodeCategory.Clip, "Pose", "Holds a single frame of a clip, e.g. the ironsight pose."),
        ["SelectorNode"] = new(GraphNodeCategory.Blend, "Selector", "Plays the first option whose condition is true."),
        ["ClipSelectorNode"] = new(GraphNodeCategory.Blend, "Clip selector", "Plays the first clip whose condition is true, e.g. partial or empty reload."),
        ["ParameterizedClipSelectorNode"] = new(GraphNodeCategory.Blend, "Clip picker by number", "Picks one of its clips from a game value (e.g. deploy_variation), with optional weights for random picks."),
        ["ParameterizedSelectorNode"] = new(GraphNodeCategory.Blend, "Picker by number", "Picks one of its options from a game value."),
        ["Blend1DNode"] = new(GraphNodeCategory.Blend, "1D blend", "Mixes its inputs along one number, e.g. idle → ironsight pose by ironsight amount."),
        ["LayerBlendNode"] = new(GraphNodeCategory.Blend, "Layer blend", "Plays a base animation with layers added or laid over it (e.g. empty-gun bone masks)."),
        ["ReferencedGraphNode"] = new(GraphNodeCategory.Graph, "Child graph", "Plays another graph here, e.g. the weapon's inspect graph."),
        ["ScaleNode"] = new(GraphNodeCategory.Pose, "Scale", "Scales bones of its input, used to hide parts with a bone mask."),
        ["SpeedScaleNode"] = new(GraphNodeCategory.Pose, "Speed scale", "Plays its input faster or slower."),
        ["TwoBoneIKNode"] = new(GraphNodeCategory.Pose, "Two-bone IK", "Moves an arm so the hand reaches a target, e.g. keeping the left hand on the weapon."),
        ["BoneMaskNode"] = new(GraphNodeCategory.Value, "Bone mask", "A named set of bones used by layers and scaling."),
        ["ConstFloatNode"] = new(GraphNodeCategory.Value, "Number", "A fixed number."),
        ["ConstIDNode"] = new(GraphNodeCategory.Value, "Name", "A fixed name, used for sync and event matching."),
        ["ConstTargetNode"] = new(GraphNodeCategory.Value, "Target", "A fixed bone target for IK."),
        ["AndNode"] = new(GraphNodeCategory.Logic, "And", "True when all its inputs are true."),
        ["OrNode"] = new(GraphNodeCategory.Logic, "Or", "True when any of its inputs is true."),
        ["NotNode"] = new(GraphNodeCategory.Logic, "Not", "Flips its input: true becomes false."),
        ["IDComparisonNode"] = new(GraphNodeCategory.Logic, "Name check", "True when a game value matches (or does not match) one of the listed names, e.g. action is action_reload."),
        ["FloatComparisonNode"] = new(GraphNodeCategory.Logic, "Number check", "True when a game value compares to a number, e.g. ammo ≤ 0."),
        ["FloatMathNode"] = new(GraphNodeCategory.Logic, "Math", "Combines two numbers (subtract, modulo…)."),
        ["FloatSwitchNode"] = new(GraphNodeCategory.Logic, "Number switch", "Gives one number when a condition is true and another when false."),
        ["IDSwitchNode"] = new(GraphNodeCategory.Logic, "Name switch", "Gives one name when a condition is true and another when false."),
        ["GraphEventConditionNode"] = new(GraphNodeCategory.Logic, "Event check", "True when a state reports a graph event, e.g. fully in idle."),
        ["IDEventConditionNode"] = new(GraphNodeCategory.Logic, "Clip event check", "True when a clip or state is sending one of the listed events."),
        ["StateCompletedConditionNode"] = new(GraphNodeCategory.Logic, "State finished", "True when a state's animation has finished (optionally a bit before the end)."),
        ["CurrentSyncEventIDNode"] = new(GraphNodeCategory.Logic, "Current sync event", "The sync event a state is in, used to continue a reload loop at the same point."),
    };

    private static readonly Dictionary<string, GraphFieldTerm> Fields = new(StringComparer.Ordinal)
    {
        ["m_boneMaskBlendInTimePercentage.m_flValue"] = new("Bone mask blend-in", "Part of the blend time (0–1) over which a start bone mask fades in."),
        ["m_transitionOptions.m_flags"] = new("Transition options", "Bit flags for sync and clamping options; leave unless you know the bits."),
        ["m_eventConditionRules.m_flags"] = new("Event rules", "Bit flags for where events are searched; leave unless you know the bits."),
        ["m_flSpeedMultiplier"] = new("Speed", "Playback speed of the clip; 1 is normal, 2 twice as fast."),
        ["m_bAllowLooping"] = new("Loops", "The clip starts again when it reaches the end."),
        ["m_bSampleRootMotion"] = new("Root motion", "Use the clip's root bone movement."),
        ["m_nStartSyncEventOffset"] = new("Start sync offset", "Which sync event to start from."),
        ["m_flDuration"] = new("Blend time", "Seconds the transition blends from the old state to the new one; 0 is an instant cut."),
        ["m_flTimeOffset"] = new("Start offset", "Where in the target state the transition starts (0–1 of its length)."),
        ["m_blendWeightEasing"] = new("Easing", "Shape of the blend over time."),
        ["m_rootMotionBlend"] = new("Root motion blend", "How root motion is mixed during the transition."),
        ["m_bCanBeForced"] = new("Can interrupt", "This transition may cut into one that is still blending."),
        ["m_nDefaultStateIndex"] = new("Default state", "The state the machine starts in (by position)."),
        ["m_comparison"] = new("Check", "How the value is compared."),
        ["m_flComparisonValue"] = new("Compare with", "The number the game value is compared to."),
        ["m_flEpsilon"] = new("Tolerance", "How close counts as equal for NearEqual."),
        ["m_comparisionIDs"] = new("Names", "Names the game value is compared with."),
        ["m_flValue"] = new("Value", "The fixed number."),
        ["m_value"] = new("Value", "The fixed name."),
        ["m_operator"] = new("Operation", "Math applied to A and B."),
        ["m_flValueB"] = new("B", "Number used when B has no input."),
        ["m_bReturnAbsoluteResult"] = new("Absolute", "Return the result without a minus sign."),
        ["m_bReturnNegatedResult"] = new("Negate", "Return the result with its sign flipped."),
        ["m_flTrueValue"] = new("If true", "Number given when the condition is true."),
        ["m_flFalseValue"] = new("If false", "Number given when the condition is false."),
        ["m_trueValue"] = new("If true", "Name given when the condition is true."),
        ["m_falseValue"] = new("If false", "Name given when the condition is false."),
        ["m_optionWeights"] = new("Weights", "Chance of each option when picked at random."),
        ["m_bIgnoreInvalidOptions"] = new("Skip empty options", "Options without a clip are not picked."),
        ["m_blendMode"] = new("Blend mode", "Additive adds the layer on top; Overlay replaces the bones it covers."),
        ["m_bIsSynchronized"] = new("Synchronized", "The layer follows the base animation's timing."),
        ["m_bIgnoreEvents"] = new("Ignore events", "Events from this layer are not sent."),
        ["m_bIsOffState"] = new("Off state", "The layer is off while this state plays."),
        ["m_flTimeValueSeconds"] = new("Time (s)", "When the timed event fires."),
        ["m_ID"] = new("Event", "The event name sent."),
        ["m_eventIDs"] = new("Events", "Event names this check looks for."),
        ["m_eventID"] = new("Event", "Event name this check looks for."),
        ["m_boneMaskID"] = new("Bone mask", "The named bone set."),
        ["m_flTransitionDurationSeconds"] = new("Finish early by (s)", "Counts as finished this many seconds before the end, so a blend can start."),
        ["m_flDefaultInputValue"] = new("Default speed", "Speed used when the input has no value."),
        ["m_flBlendTimeSeconds"] = new("Blend time", "Seconds to blend the IK in."),
        ["m_flChainRotationWeight"] = new("Chain rotation", "How much the arm chain rotates."),
    };

    private static readonly Dictionary<string, string[]> Choices = new(StringComparer.Ordinal)
    {
        ["m_blendWeightEasing"] = ["Linear", "InOutQuad"],
        ["m_rootMotionBlend"] = ["Blend"],
        ["m_operator"] = ["Add", "Sub", "Mul", "Div", "Mod"],
        ["m_blendMode"] = ["Additive", "Overlay"],
        ["m_comparisionOperator"] = ["LessThanEqual", "GreaterThanEqual"],
        ["m_eventTypeCondition"] = ["FullyInState"],
    };

    /// <summary>Structural fields: indices that wire nodes together. Read-only in the editor.</summary>
    private static readonly HashSet<string> Structural = new(StringComparer.Ordinal)
    {
        "m_nNodeIdx", "m_nDataSlotIdx", "m_nReferencedGraphIdx", "m_nTargetStateIdx", "m_nInputIdx0", "m_nInputIdx1",
    };

    public static GraphNodeTerm Describe(string shortClass) =>
        Nodes.TryGetValue(shortClass, out var term) ? term : new GraphNodeTerm(GraphNodeCategory.Logic, shortClass.Replace("Node", ""), "No description yet.");

    public static GraphFieldTerm Field(string path)
    {
        var key = GraphDocument.LeafKey(path);
        // Nested values such as m_boneMaskBlendInTimePercentage.m_flValue have their own names.
        var parts = System.Text.RegularExpressions.Regex.Replace(path, @"\[\d+\]", "").Split('.');
        if (parts.Length >= 2 && Fields.TryGetValue(parts[^2] + "." + parts[^1], out var nested))
        {
            return nested;
        }

        var index = path.EndsWith(']') ? " " + path[path.LastIndexOf('[')..] : "";
        if (Fields.TryGetValue(key, out var term))
        {
            return term with { Name = term.Name + index };
        }

        return new GraphFieldTerm(Readable(key) + index, key);
    }

    /// <summary>m_nChildNodeIdx → "Child", m_conditionNodeIndices → "Condition", m_flFoo → "Foo".</summary>
    private static string Readable(string key)
    {
        var name = key.StartsWith("m_", StringComparison.Ordinal) ? key[2..] : key;
        foreach (var prefix in new[] { "fl", "sz", "n", "b", "e" })
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[prefix.Length]))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        foreach (var suffix in new[] { "ValueNodeIdx", "NodeIndices", "NodeIdx" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        var spaced = string.Concat(name.Select((character, position) => position > 0 && char.IsUpper(character) ? " " + char.ToLowerInvariant(character) : character.ToString()));
        return spaced.Length == 0 ? key : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    /// <summary>Values that tune behaviour without rewiring the graph.</summary>
    public static bool IsEditable(string key, KVValueType type)
    {
        if (GraphDocument.IsLink(key) || Structural.Contains(key))
        {
            return false;
        }

        return type is KVValueType.Boolean or KVValueType.String or KVValueType.FloatingPoint or KVValueType.FloatingPoint64
            or KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64 or KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64;
    }

    /// <summary>The allowed values of an enum field, or null for free text and numbers.</summary>
    public static IReadOnlyList<string>? ChoicesFor(string shortClass, string path)
    {
        var key = GraphDocument.LeafKey(path);
        if (key == "m_comparison")
        {
            return shortClass == "IDComparisonNode"
                ? ["Matches", "DoesntMatch"]
                : ["GreaterThanEqual", "LessThanEqual", "NearEqual", "GreaterThan", "LessThan"];
        }

        return Choices.TryGetValue(key, out var choices) ? choices : null;
    }
}
