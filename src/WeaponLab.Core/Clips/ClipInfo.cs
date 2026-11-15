namespace WeaponLab.Core.Clips;

/// <summary>
/// Decoded summary of a compiled <c>.vnmclip_c</c>. Pose data is not
/// included; it is decoded only when a clip is imported for editing.
/// </summary>
/// <param name="ResourcePath">VPK path of the clip.</param>
/// <param name="Sha256">Hash of the compiled bytes, used as the template fingerprint.</param>
/// <param name="FrameCount">Number of frames, including the last.</param>
/// <param name="DurationSeconds">Time from the first to the last frame.</param>
/// <param name="Skeleton">Primary skeleton, normally the viewmodel hand rig.</param>
/// <param name="SecondarySkeletons">Skeletons of paired streams, normally the weapon.</param>
/// <param name="TrackCount">Bone tracks in the primary stream.</param>
public sealed record ClipInfo(
    string ResourcePath,
    string Sha256,
    int FrameCount,
    double DurationSeconds,
    bool IsAdditive,
    string Skeleton,
    IReadOnlyList<string> SecondarySkeletons,
    int TrackCount,
    IReadOnlyList<ClipEvent> Events)
{
    public const string ViewmodelSkeleton = "animation/skeletons/characters/viewmodel.vnmskel";

    /// <summary>The clip's sync track sections (<c>m_syncTrack.m_syncEvents</c>), normalized like the events.</summary>
    public IReadOnlyList<SyncSection> SyncTrack { get; init; } = [];

    public string Name => ResourcePath[(ResourcePath.LastIndexOf('/') + 1)..].Replace(".vnmclip_c", "");

    public int LastFrame => Math.Max(FrameCount - 1, 0);

    public double FramesPerSecond => DurationSeconds > 0 ? LastFrame / DurationSeconds : 0;

    /// <summary>
    /// True for the clips the editor supports: the viewmodel hand rig with
    /// at most one paired weapon stream (same rule as CS2AL).
    /// </summary>
    public bool IsEditable => Skeleton == ViewmodelSkeleton && SecondarySkeletons.Count <= 1;

    /// <summary>The third-person body skeleton (75 bones; verified from files 2026-11-14).</summary>
    public const string WorldSkeleton = "animation/skeletons/characters/worldmodel.vnmskel";

    /// <summary>
    /// The third-person clips the editor supports (0.26): the body skeleton with at most one weapon stream, not
    /// additive (idle and shoot add onto other poses, and how the game builds their base is unknown, so they stay
    /// read-only) and not a <c>+non_additive</c> twin (no graph plays those).
    /// </summary>
    public bool IsEditableWorld => Skeleton == WorldSkeleton && SecondarySkeletons.Count <= 1 && !IsAdditive && !Name.Contains('+', StringComparison.Ordinal);
}
