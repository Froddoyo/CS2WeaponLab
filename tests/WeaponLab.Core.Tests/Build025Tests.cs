using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Tests;

/// <summary>0.25 animation files: decoding a compiled clip with bone names and round-tripping it through a pose file. Skipped without CS2.</summary>
public sealed class Build025Tests
{
    private const string AkReload = "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c";

    private static GameConnection? TryConnect() => GameLocator.Locate() is null ? null : GameConnection.Open();

    [Theory]
    [InlineData("reload.vnmclip_c", "compiled")]
    [InlineData("reload.json.gz", "poses")]
    [InlineData("Reload.BLEND", "keys")]
    [InlineData("reload.fbx", "fbx")]
    public void FormatsFollowTheExtension(string file, string format) => Assert.Equal(format, AnimationTransfer.FormatOf(file));

    [Fact]
    public void UnknownExtensionsAreRefused() => Assert.Throws<ArgumentException>(() => AnimationTransfer.FormatOf("reload.txt"));

    /// <summary>A CS2 clip decodes into named arms and weapon streams, and a pose file written from it reads back the same bones and poses.</summary>
    [Fact]
    public void CompiledClipRoundTripsThroughAPoseFile()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var skeletons = AnimationFiles.Skeletons(connection, null, null);
        var source = AnimationFiles.FromClip(connection.Archive.ReadBytes(AkReload), AkReload, skeletons, "animation/skeletons/weapons/ak47.vnmskel");
        Assert.NotNull(source.Arms);
        Assert.NotNull(source.Weapon);
        Assert.Equal(connection.ReadSkeleton(ClipInfo.ViewmodelSkeleton).Bones.Count, source.Arms.Bones.Count);
        Assert.Contains("hand_R", source.Arms.Bones);
        Assert.Equal(source.Arms.Frames, source.Weapon.Frames);

        var file = Path.Combine(Path.GetTempPath(), $"weaponlab-anim-{Guid.NewGuid():N}.json.gz");
        try
        {
            AnimationFiles.Write(file, source);
            var back = AnimationFiles.FromPoseFile(file, _ => null); // Names and parents come from the file itself.
            Assert.Equal(source.Arms.Bones, back.Arms!.Bones);
            Assert.Equal(source.Arms.Parents, back.Arms.Parents);
            Assert.Equal(source.Weapon.Bones, back.Weapon!.Bones);
            Assert.Equal(source.Frames, back.Frames);
            var (a, b) = (source.Arms.Poses[source.Frames / 2][5], back.Arms.Poses[source.Frames / 2][5]);
            Assert.Equal(a.Translation.X, b.Translation.X, 4);
            Assert.Equal(a.Rotation.W, b.Rotation.W, 4);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
