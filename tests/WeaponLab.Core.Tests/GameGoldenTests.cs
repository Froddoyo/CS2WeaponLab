using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Tests;

/// <summary>
/// Golden tests against the installed game's clips. Skipped on machines
/// without CS2. Nothing is written to the game folder.
/// </summary>
public sealed class GameGoldenTests
{
    private static GameConnection? TryConnect()
    {
        return GameLocator.Locate() is null ? null : GameConnection.Open();
    }

    [Fact]
    public void EveryGameClipPassesTheEncoderSelfTest()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var failures = connection.Weapons
            .SelectMany(weapon => weapon.ClipPaths)
            .Select(path => EncoderSelfTest.Run(connection.Archive.ReadBytes(path), path))
            .Where(result => !result.Passed)
            .Select(result => $"[{result.Check}] {result.ResourcePath}: {result.Message}")
            .ToList();

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryStreamHasOneTrackPerSkeletonBone()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var mismatches = new List<string>();
        foreach (var path in connection.Weapons.SelectMany(weapon => weapon.ClipPaths))
        {
            using var template = connection.OpenClipTemplate(path);
            foreach (var stream in template.Streams)
            {
                var bones = connection.ReadSkeleton(stream.Skeleton).Bones.Count;
                if (bones != stream.TrackCount)
                {
                    mismatches.Add($"{path} {stream.Label}: {stream.TrackCount} tracks, {bones} bones");
                }
            }
        }

        Assert.Empty(mismatches);
    }

    /// <summary>Poses of another length are refused unless the edit sets the clip's length (ClipLengthTests cover that).</summary>
    [Fact]
    public void WriterRejectsAnotherLengthItWasNotTold()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string path = "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c";
        var bytes = connection.Archive.ReadBytes(path);
        using var template = ClipTemplate.Open(bytes, path);
        var poses = template.Primary.ReadPoses();

        var error = Assert.Throws<ClipWriteException>(() => ClipWriter.Write(bytes, path, new ClipEdit { Primary = poses[..^1] }));
        Assert.Contains("Set the clip's length", error.Message);
    }
}
