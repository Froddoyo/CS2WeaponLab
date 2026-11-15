using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Tests;

public sealed class SandboxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ManifestRoundTripsThroughJson()
    {
        var manifest = new SandboxManifest
        {
            WeaponId = "rifle_ak",
            Category = "rifle",
            VpkFolder = "animation/anims/viewmodel/rifle/rifle_ak",
            TargetAddon = null,
            CreatedUtc = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero),
            Game = new SandboxGameBuild("1.41.9.2", "2000935", "11162119"),
            Clips = [new SandboxClip("a/b.vnmclip_c", "templates/a/b.vnmclip_c", "00ff", 74, 2.4333, true)],
        };

        var json = manifest.ToJson();
        var parsed = SandboxManifest.FromJson(json);

        Assert.Contains("\"schema\": \"weaponlab.sandbox/1\"", json);
        Assert.Contains("\"targetAddon\": null", json);
        Assert.Equal(manifest.WeaponId, parsed.WeaponId);
        Assert.Equal(manifest.Game, parsed.Game);
        Assert.Equal(manifest.Clips, parsed.Clips);
    }

    [Fact]
    public void ManifestWithUnknownSchemaIsRejected()
    {
        var json = """{ "schema": "weaponlab.sandbox/99", "weaponId": "x", "category": "", "vpkFolder": "", "createdUtc": "2026-10-23T00:00:00Z", "game": { "patchVersion": "", "clientVersion": "", "sourceRevision": "" }, "clips": [] }""";

        Assert.Throws<InvalidDataException>(() => SandboxManifest.FromJson(json));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    public void SafeJoinRejectsEscapes(string relative)
    {
        Assert.Throws<InvalidDataException>(() => AtomicFile.SafeJoin(_root, relative));
    }

    [Fact]
    public void AtomicWriteReplacesAndLeavesNoTempFiles()
    {
        var path = Path.Combine(_root, "nested", "file.bin");
        AtomicFile.WriteAllBytes(path, [1, 2, 3]);
        AtomicFile.WriteAllBytes(path, [4, 5]);

        Assert.Equal([4, 5], File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void RepositoryRootIsFoundFromBuildOutput()
    {
        var buildOutput = Path.Combine(_root, "src", "WeaponLab.App", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(buildOutput);
        File.WriteAllText(Path.Combine(_root, "CS2WeaponLab.slnx"), "<Solution />");

        Assert.Equal(Path.GetFullPath(_root), SandboxStore.FindRepositoryRoot(buildOutput));
    }

    [Fact]
    public void RepositoryRootIsNullOutsideTheRepository()
    {
        Directory.CreateDirectory(_root);

        Assert.Null(SandboxStore.FindRepositoryRoot(_root));
    }

    [Fact]
    public void ListIsEmptyForNewWorkspace()
    {
        Assert.Empty(new SandboxStore(_root).List());
    }

    /// <summary>
    /// Uses the installed game; skipped on machines without CS2.
    /// </summary>
    [Fact]
    public void CreateCopiesTemplatesThatMatchTheGame()
    {
        if (GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        using var connection = GameConnection.Open();
        var weapon = connection.FindWeapon("rifle_ak") ?? throw new InvalidOperationException("rifle_ak missing from the game.");
        var store = new SandboxStore(_root);

        var sandbox = store.Create(connection, weapon, targetAddon: null);

        Assert.True(store.Exists("rifle_ak"));
        Assert.Equal(weapon.ClipPaths.Count, sandbox.Manifest.Clips.Count);
        foreach (var clip in sandbox.Manifest.Clips)
        {
            var bytes = File.ReadAllBytes(AtomicFile.SafeJoin(sandbox.FolderPath, clip.Template));
            Assert.Equal(clip.Sha256, GameArchive.Sha256(bytes));
        }

        Assert.Empty(SandboxStore.ChangedSinceImport(store.Open("rifle_ak"), connection.Archive));
        Assert.Throws<IOException>(() => store.Create(connection, weapon, targetAddon: null));
        Assert.False(Directory.Exists(Path.Combine(_root, ".staging")) && Directory.EnumerateFileSystemEntries(Path.Combine(_root, ".staging")).Any());
    }
}
