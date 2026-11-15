using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Tests;

/// <summary>Adding, removing and copying clip events, sync tracks and the shell-reload helper. Game-backed tests skip without CS2.</summary>
public sealed class EventListTests : IDisposable
{
    private const string SsgReload = "animation/anims/viewmodel/rifle/rifle_ssg08/reload_ssg08.vnmclip_c";
    private const string NovaReload = "animation/anims/viewmodel/rifle/rifle_nova/reload_nova.vnmclip_c";
    private const string AkReload = "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c";
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
    public void ShellLoopProblemsNameTheShellFrame()
    {
        Assert.Empty(ShellReload.Problems(new ShellLoop(52, 61, 56), 111));
        Assert.Contains(ShellReload.Problems(new ShellLoop(52, 61, 60), 111), problem => problem.Contains("at least 4 frames"));
        Assert.NotEmpty(ShellReload.Problems(new ShellLoop(0, 61, 56), 111));
        Assert.NotEmpty(ShellReload.Problems(new ShellLoop(52, 111, 56), 111));
        Assert.Equal("a2", ShellReload.NextId([new AddedEvent("a1", "CNmIDEvent", "X", 0, 0), new AddedEvent("a3", "CNmIDEvent", "Y", 0, 0)]));
    }

    /// <summary>The helper turns the SSG08 reload into the recipe verified in game (Annabelle, 2026-10-31) and touches nothing else.</summary>
    [Fact]
    public void ShellReloadWritesTheNovaRecipe()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(SsgReload);
        var before = ClipReader.Read(bytes, SsgReload);
        var edits = ShellReload.Apply(before, StoredEventEdits.Empty, new ShellLoop(52, 61, 56), silentReload: false);
        var result = ClipWriter.Write(bytes, SsgReload, new ClipEdit { Events = edits.Changes, EventList = edits.List });
        var after = ClipReader.Read(result.Bytes, SsgReload);

        double Frame(string id) => after.Events.Single(clipEvent => clipEvent.Name == id).StartFrame(after);
        Assert.Equal([ShellReload.Intro, ShellReload.Loop, ShellReload.Outro], after.SyncTrack.Select(section => section.Id));
        Assert.Equal(52, after.SyncTrack[1].StartTime * after.LastFrame, precision: 3);
        Assert.Equal(61, after.SyncTrack[2].StartTime * after.LastFrame, precision: 3);
        Assert.Equal(0, Frame(ShellReload.Intro), precision: 3);
        Assert.Equal(52, Frame(ShellReload.Loop), precision: 3);
        Assert.Equal(56, Frame(ShellReload.AddAmmo), precision: 3);
        Assert.Equal(61, Frame(ShellReload.Outro), precision: 3);
        Assert.Equal(0, Frame(ShellReload.Silent), precision: 3);
        Assert.DoesNotContain(after.Events, clipEvent => clipEvent.Name == ShellReload.DropMag);
        Assert.Contains(result.RemovedEvents, removed => removed.Contains(ShellReload.DropMag));
        Assert.True(result.SyncTrackChanged);

        // Sorted by start time, sounds as they were, poses untouched.
        Assert.Equal(after.Events.Select(clipEvent => clipEvent.StartTime).Order(), after.Events.Select(clipEvent => clipEvent.StartTime));
        Assert.Equal(
            before.Events.Where(clipEvent => clipEvent.Kind == ClipEventKind.Sound).Select(clipEvent => (clipEvent.Name, clipEvent.StartTime, clipEvent.Fields)),
            after.Events.Where(clipEvent => clipEvent.Kind == ClipEventKind.Sound).Select(clipEvent => (clipEvent.Name, clipEvent.StartTime, clipEvent.Fields)));
        Assert.All(result.Streams, stream => Assert.Empty(stream.ChangedTracks));

        // The loop markers carry the same fields as the Nova's own.
        var nova = ClipReader.Read(connection.Archive.ReadBytes(NovaReload), NovaReload);
        Assert.Equal(
            nova.Events.First(clipEvent => clipEvent.Name == ShellReload.Loop).Fields.Values.Keys,
            after.Events.First(clipEvent => clipEvent.Name == ShellReload.Loop).Fields.Values.Keys);
        Assert.Equal(ShellReload.Loop, after.Events.First(clipEvent => clipEvent.Name == ShellReload.Loop).Fields["m_syncID"]);

        // The helper reads its own loop back.
        Assert.Equal(new ShellLoop(52, 61, 56), ShellReload.Read(after.SyncTrack, after.Events, after.LastFrame));
    }

    [Fact]
    public void AddedAndRemovedEventsLeaveTheRestAlone()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(AkReload);
        var before = ClipReader.Read(bytes, AkReload);
        var dropMag = before.Events.First(clipEvent => clipEvent.Name == ShellReload.DropMag);
        var sound = before.Events.First(clipEvent => clipEvent.Kind == ClipEventKind.Sound);
        var list = new EventListEdit
        {
            Removed = [dropMag.Index],
            Added = [new AddedEvent("a1", "CNmSoundEvent", "WeaponLab.Test", 0.3, 0) { Fields = new Dictionary<string, string> { ["m_position"] = "Entity" } }],
        };
        var result = ClipWriter.Write(bytes, AkReload, new ClipEdit { Events = [new EventChange(sound.Index, null, 0.05)], EventList = list });
        var after = ClipReader.Read(result.Bytes, AkReload);

        Assert.Equal(before.Events.Count, after.Events.Count);
        var added = after.Events.Single(clipEvent => clipEvent.Name == "WeaponLab.Test");
        Assert.Equal(0.3, added.StartTime, precision: 6);
        Assert.Equal("Entity", added.Fields["m_position"]);
        Assert.Equal("", added.Fields["m_attachmentName"]);
        Assert.Equal(0.05, after.Events.Single(clipEvent => clipEvent.Name == sound.Name && Math.Abs(clipEvent.StartTime - 0.05) < 1e-6).StartTime, precision: 6);

        // Every other template event is still there with the same values.
        foreach (var clipEvent in before.Events.Where(clipEvent => clipEvent.Index != dropMag.Index && clipEvent.Index != sound.Index))
        {
            Assert.Contains(after.Events, other => other.Name == clipEvent.Name && other.StartTime == clipEvent.StartTime && other.Fields.Equals(clipEvent.Fields));
        }

        Assert.Equal(before.SyncTrack, after.SyncTrack);
    }

    /// <summary>A clip with no gameplay event still takes one: it starts from the fields CS2's clips use.</summary>
    [Fact]
    public void ANewEventClassStartsFromCs2sFields()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var weapon = connection.FindWeapon("rifle_ak")!;
        var path = weapon.ClipPaths.First(candidate => ClipReader.Read(connection.Archive, candidate).Events.All(clipEvent => clipEvent.Kind != ClipEventKind.Gameplay));
        var bytes = connection.Archive.ReadBytes(path);
        var result = ClipWriter.Write(bytes, path, new ClipEdit
        {
            EventList = new EventListEdit { Added = [new AddedEvent("a1", "CNmIDEvent", "WPN_WEAPONLAB_TEST", 0.25, 0.1)] },
        });

        var added = ClipReader.Read(result.Bytes, path).Events.Single(clipEvent => clipEvent.Name == "WPN_WEAPONLAB_TEST");
        var nova = ClipReader.Read(connection.Archive.ReadBytes(NovaReload), NovaReload).Events.First(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay);
        Assert.Equal(nova.Fields.Values.Keys, added.Fields.Values.Keys);
        Assert.Equal("WPN_WEAPONLAB_TEST", added.Fields["m_syncID"]);
        Assert.Equal(0.1, added.Duration, precision: 6);
    }

    [Fact]
    public void BadSyncTracksAndAddedEventsAreRefused()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(SsgReload);
        Assert.Throws<ClipWriteException>(() => ClipWriter.Write(bytes, SsgReload, new ClipEdit
        {
            EventList = new EventListEdit { Sync = [new SyncSection(ShellReload.Intro, 0, 0.6), new SyncSection(ShellReload.Loop, 0.5, 0.2)] },
        }));
        Assert.Throws<ClipWriteException>(() => ClipWriter.Write(bytes, SsgReload, new ClipEdit
        {
            EventList = new EventListEdit { Added = [new AddedEvent("a1", "CNmSoundEvent", "has space", 0.2, 0)] },
        }));
        Assert.Throws<ClipWriteException>(() => ClipWriter.Write(bytes, SsgReload, new ClipEdit
        {
            EventList = new EventListEdit { Added = [new AddedEvent("a1", "CNmIDEvent", "WPN_X", 0.95, 0.2)] },
        }));

        // The clip's own sync track asked for again changes nothing.
        var own = ClipReader.Read(bytes, SsgReload).SyncTrack;
        Assert.True(ClipWriter.Write(bytes, SsgReload, new ClipEdit { EventList = new EventListEdit { Sync = own } }).IdenticalToTemplate);
    }

    [Fact]
    public void CopyingTheNovasMarkersGivesTheNovasLoop()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var nova = ClipReader.Read(connection.Archive.ReadBytes(NovaReload), NovaReload);
        var markers = nova.Events.Where(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay).Select(clipEvent => clipEvent.Index).ToList();
        var edits = EventCopy.Into(StoredEventEdits.Empty, nova, markers, syncTrack: true);

        var bytes = connection.Archive.ReadBytes(SsgReload);
        var after = ClipReader.Read(ClipWriter.Write(bytes, SsgReload, new ClipEdit { Events = edits.Changes, EventList = edits.List }).Bytes, SsgReload);
        Assert.Equal(nova.SyncTrack, after.SyncTrack);
        foreach (var marker in nova.Events.Where(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay))
        {
            Assert.Contains(after.Events, clipEvent => clipEvent.Name == marker.Name && Math.Abs(clipEvent.StartTime - marker.StartTime) < 1e-6);
        }
    }

    [Fact]
    public void SoundEventsResolveToPlayableFiles()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var library = Sounds.SoundLibrary.Load(connection, addonGamePath: null);
        Assert.True(library.Names.Count > 1000);
        var info = library.Find("Weapon_Nova.Insertshell")!;
        Assert.Equal(3, info.Files.Count);
        Assert.Equal(0.3, info.Volume, precision: 3);
        var sound = library.Decode(info.Files[0])!;
        Assert.True(sound.IsMp3);
        Assert.True(sound.Data.Length > 1000);
        Assert.Null(library.Find("WeaponLab.NoSuchSound"));
    }

    /// <summary>The sandbox file keeps added and removed events and the sync track, and still reads 0.18's plain list.</summary>
    [Fact]
    public void StoredEditsRoundTripAndReadOldFiles()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var store = new SandboxStore(Path.Combine(_root, "workspace"));
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        var path = EventEdits.PathFor(sandbox, "reload_ak");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[ { \"index\": 2, \"name\": null, \"startTime\": 0.5 } ]");
        static (int, string?, double?) Key(EventChange change) => (change.Index, change.Name, change.StartTime);
        Assert.Equal([(2, null, 0.5)], EventEdits.Read(sandbox, "reload_ak").Changes.Select(Key));
        Assert.True(EventEdits.Read(sandbox, "reload_ak").List.IsEmpty);

        var edits = new StoredEventEdits(
            [new EventChange(2, null, 0.5)],
            new EventListEdit
            {
                Removed = [3],
                Added = [new AddedEvent("a1", "CNmIDEvent", "WPN_X", 0.4, 0) { Fields = new Dictionary<string, string> { ["m_secondaryID"] = "clip" } }],
                Sync = [new SyncSection(ShellReload.Intro, 0, 0.5), new SyncSection(ShellReload.Loop, 0.5, 0.5)],
            });
        EventEdits.Save(sandbox, "reload_ak", edits);
        var read = EventEdits.Read(sandbox, "reload_ak");
        Assert.Equal(edits.Changes.Select(Key), read.Changes.Select(Key));
        Assert.Equal(edits.List.Removed, read.List.Removed);
        Assert.Equal(edits.List.Sync, read.List.Sync);
        Assert.Equal("clip", read.List.Added.Single().Fields["m_secondaryID"]);

        // Saving only changes keeps the rest; an empty edit removes the file.
        EventEdits.Save(sandbox, "reload_ak", []);
        Assert.Single(EventEdits.Read(sandbox, "reload_ak").List.Added);
        EventEdits.Save(sandbox, "reload_ak", StoredEventEdits.Empty);
        Assert.False(File.Exists(path));
    }
}
