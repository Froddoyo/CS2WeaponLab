using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Tests;

/// <summary>Changing a clip's frame count: events and sections keep their frames, poses are cut or held. Game-backed tests skip without CS2.</summary>
public sealed class ClipLengthTests : IDisposable
{
    private const string SsgReload = "animation/anims/viewmodel/rifle/rifle_ssg08/reload_ssg08.vnmclip_c";
    private const string SsgInspect = "animation/anims/viewmodel/rifle/rifle_ssg08/lookat01_ssg08.vnmclip_c";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static GameConnection? TryConnect() => GameLocator.Locate() is null ? null : GameConnection.Open();

    [Fact]
    public void SectionsKeepTheirFramesAndTheLastEndsAtTheEnd()
    {
        // INTRO 0-11, LOOP 11-24, OUTRO 24-111 in a 112-frame clip, cut to 50 frames.
        IReadOnlyList<SyncSection> sections = [new("INTRO", 0, 11 / 111.0), new("LOOP", 11 / 111.0, 13 / 111.0), new("OUTRO", 24 / 111.0, 87 / 111.0)];
        var cut = ClipLength.Rescale(sections, 111, 49);
        Assert.Equal(3, cut.Count);
        Assert.Equal(11, cut[1].StartTime * 49, precision: 6);
        Assert.Equal(24, cut[1].EndTime * 49, precision: 6);
        Assert.Equal(1, cut[2].EndTime, precision: 9);

        // A section starting past the new end is dropped; lengthening stretches the last one to the end.
        var short10 = ClipLength.Rescale(sections, 111, 9);
        Assert.Single(short10);
        Assert.Equal(1, short10[0].EndTime, precision: 9);
        var longer = ClipLength.Rescale(sections, 111, 200);
        Assert.Equal(11, longer[1].StartTime * 200, precision: 6);
        Assert.Equal(1, longer[2].EndTime, precision: 9);

        Assert.Equal(1, ClipLength.Rescale(82 / 111.0, 111, 49), precision: 9); // An event past the new end moves to it.
        Assert.Equal([0, 1, 2, 2, 2], ClipLength.Resample([0, 1, 2], 5));
    }

    /// <summary>
    /// The SSG08 reload cut from 112 to 50 frames (the Nova's length): the clip
    /// decodes with 50 frames at the same frame rate, the template's poses for
    /// those frames, events on their frames (one past the end at the end), one
    /// sync section over the whole clip; and edits stored at that length write.
    /// </summary>
    [Fact]
    public void ReloadCutToFiftyFramesKeepsFramesAndEvents()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(SsgReload);
        var stock = ClipReader.Read(bytes, SsgReload);
        var result = ClipWriter.Write(bytes, SsgReload, new ClipEdit { FrameCount = 50 });
        Assert.False(result.IdenticalToTemplate);

        var cut = ClipReader.Read(result.Bytes, SsgReload);
        Assert.Equal(50, cut.FrameCount);
        Assert.Equal(stock.FramesPerSecond, cut.FramesPerSecond, precision: 3);
        Assert.Equal(stock.Events.Count, cut.Events.Count);
        for (var index = 0; index < stock.Events.Count; index++)
        {
            Assert.Equal(Math.Min(stock.Events[index].StartFrame(stock), 49), cut.Events[index].StartFrame(cut), precision: 3);
        }

        Assert.Single(cut.SyncTrack);
        Assert.Equal(1, cut.SyncTrack[0].EndTime, precision: 6);

        using (var before = ClipTemplate.Open(bytes, SsgReload))
        using (var after = ClipTemplate.Open(result.Bytes, SsgReload))
        {
            var (old, now) = (before.Primary.ReadPoses(), after.Primary.ReadPoses());
            for (var frame = 0; frame < 50; frame++)
            {
                for (var track = 0; track < now[frame].Length; track++)
                {
                    Assert.True(PoseTolerance.Default.Accepts(old[frame][track], now[frame][track]), $"frame {frame} track {track}");
                }
            }

            Assert.Equal(50, after.Secondary[0].FrameCount);
        }

        // Shell-loop edits made at 50 frames (the Annabelle's Nova timing) write at that length.
        var edits = ClipLength.SetFrames(stock, StoredEventEdits.Empty, 50);
        var atFifty = ClipLength.At(stock, 50);
        var loop = ShellReload.Apply(atFifty, edits, new ShellLoop(11, 24, 20), silentReload: false);
        Assert.Equal(50, loop.Frames);
        var written = ClipReader.Read(ClipWriter.Write(bytes, SsgReload, new ClipEdit { Events = loop.Changes, EventList = loop.List, FrameCount = loop.Frames }).Bytes, SsgReload);
        var read = ShellReload.Read(written.SyncTrack, written.Events, written.LastFrame);
        Assert.Equal(new ShellLoop(11, 24, 20), read);
    }

    /// <summary>The SSG08 inspect lengthened from 160 to 169 frames: the added frames hold the last pose.</summary>
    [Fact]
    public void LongerClipHoldsItsLastPose()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(SsgInspect);
        var stock = ClipReader.Read(bytes, SsgInspect);
        var longer = ClipWriter.Write(bytes, SsgInspect, new ClipEdit { FrameCount = stock.FrameCount + 9 }).Bytes;
        using var template = ClipTemplate.Open(longer, SsgInspect);
        var poses = template.Primary.ReadPoses();
        Assert.Equal(stock.FrameCount + 9, poses.Length);
        for (var frame = stock.FrameCount; frame < poses.Length; frame++)
        {
            for (var track = 0; track < poses[frame].Length; track++)
            {
                Assert.True(PoseTolerance.Default.Accepts(poses[stock.LastFrame][track], poses[frame][track]));
            }
        }
    }

    /// <summary>The length is stored with the clip's event edits, and edited times keep their frames when it changes.</summary>
    [Fact]
    public void LengthIsStoredWithTheEditsAndKeepsEditedFrames()
    {
        var events = new List<ClipEvent> { new(0, ClipEventKind.Sound, "CNmSoundEvent", "Weapon_SSG08.Clipin", "", 45 / 111.0, 0) };
        var template = new ClipInfo(SsgReload, "00", 112, 3.7, false, ClipInfo.ViewmodelSkeleton, [], 56, events);
        var current = new StoredEventEdits([new EventChange(0, null, 20 / 111.0)], EventListEdit.None);

        var fifty = ClipLength.SetFrames(template, current, 50);
        Assert.Equal(50, fifty.Frames);
        Assert.Equal(20, fifty.Changes[0].StartTime!.Value * 49, precision: 6);
        Assert.Null(ClipLength.SetFrames(template, fifty, 112).Frames); // Back to the template's length.
        Assert.Throws<ClipWriteException>(() => ClipLength.SetFrames(template, current, 1));

        var sandbox = new SandboxInfo(_root, new SandboxManifest
        {
            WeaponId = "rifle_ssg08",
            Category = "rifle",
            VpkFolder = "animation/anims/viewmodel/rifle/rifle_ssg08",
            TargetAddon = null,
            CreatedUtc = DateTimeOffset.UtcNow,
            Game = new SandboxGameBuild("1", "2", "3"),
            Clips = [],
        });
        EventEdits.Save(sandbox, "reload_ssg08", fifty);
        Assert.Equal(50, EventEdits.Read(sandbox, "reload_ssg08").Frames);
        EventEdits.Save(sandbox, "reload_ssg08", StoredEventEdits.Empty with { Frames = 50 });
        Assert.True(File.Exists(EventEdits.PathFor(sandbox, "reload_ssg08"))); // A length alone is an edit.
    }
}
