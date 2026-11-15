using System.Numerics;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Tests;

public sealed class ExportTests : IDisposable
{
    private static readonly Quaternion Turn = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 10 * MathF.PI / 180);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void NoiseBelowThresholdsIsNotAnEdit()
    {
        var stock = new BonePose(new Vector3(1, 2, 3), Quaternion.Identity, 1);
        var reference = new BonePose(new Vector3(5, 5, 5), Quaternion.Identity, 1);
        var edited = reference with { Translation = reference.Translation + new Vector3(1e-6f, 0, 0) };

        Assert.Null(EditDeltas.ApplyOne(stock, reference, edited, additive: false));
    }

    [Fact]
    public void EditsApplyAsDeltasOntoStock()
    {
        var stockRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);
        var stock = new BonePose(new Vector3(1, 2, 3), stockRotation, 1);
        // Blender's reference differs slightly from stock (float noise); only the edit should carry over.
        var reference = new BonePose(new Vector3(1.00001f, 2, 3), stockRotation, 1);
        var edited = new BonePose(reference.Translation + new Vector3(0, 0.5f, 0), Quaternion.Normalize(Turn * stockRotation), 2);

        var result = EditDeltas.ApplyOne(stock, reference, edited, additive: false)!.Value;

        Assert.Equal(new Vector3(1, 2.5f, 3), result.Translation);
        Assert.True(BonePose.RotationDistance(Quaternion.Normalize(Turn * stockRotation), result.Rotation) < 1e-5f);
        Assert.Equal(2, result.Scale, precision: 5);
    }

    [Fact]
    public void AdditiveScaleEditsAdd()
    {
        var stock = new BonePose(Vector3.Zero, Quaternion.Identity, 0);
        var reference = stock with { Scale = 1 };
        var edited = stock with { Scale = 1.5f };

        Assert.Equal(0.5f, EditDeltas.ApplyOne(stock, reference, edited, additive: true)!.Value.Scale, precision: 5);
    }

    [Fact]
    public void RetimedSamplesAreRejected()
    {
        var frames = new[] { new[] { new BonePose(Vector3.Zero, Quaternion.Identity, 1) } };

        Assert.Throws<ClipWriteException>(() => EditDeltas.Apply(frames, [], frames, additive: false, "arms"));
    }

    /// <summary>
    /// Exports the AK's reload with arm_lower_L turned 10°: only that bone may
    /// change, and an unedited export must be byte-identical. Skipped without CS2.
    /// </summary>
    [Fact]
    public void ExportChangesOnlyTheEditedBone()
    {
        if (GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        using var connection = GameConnection.Open();
        var sandbox = new SandboxStore(_root).Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        var package = RigPackage.Write(sandbox, connection);

        // Stand in for Blender: the stock poses serve as both reference and unedited sample.
        var stockFile = Path.Combine(package.InputPath, "poses", "reload_ak.json.gz");
        Directory.CreateDirectory(Path.Combine(package.InputPath, "reference"));
        File.Copy(stockFile, Path.Combine(package.InputPath, "reference", "reload_ak.json.gz"));

        var unedited = ClipExporter.Export(sandbox, "reload_ak", stockFile, toSandbox: true, extraOutputPath: null);
        Assert.True(unedited.IdenticalToTemplate);
        var template = File.ReadAllBytes(Path.Combine(sandbox.FolderPath, "templates", unedited.ResourcePath));
        Assert.Equal(template, File.ReadAllBytes(unedited.SandboxOutput!));

        var poses = PoseFile.Read(stockFile);
        var arms = poses["arms"];
        var elbow = connection.ReadSkeleton(ClipInfo.ViewmodelSkeleton).Bones.Single(bone => bone.Name == "arm_lower_L").Index;
        foreach (var frame in arms)
        {
            frame[elbow] = frame[elbow] with { Rotation = Quaternion.Normalize(Turn * frame[elbow].Rotation) };
        }

        var editedFile = Path.Combine(_root, "edited.json.gz");
        PoseFile.Write(editedFile, new Dictionary<string, (string, BonePose[][])>
        {
            ["arms"] = (ClipInfo.ViewmodelSkeleton, arms),
            ["weapon"] = ("animation/skeletons/weapons/ak47.vnmskel", poses["weapon"]),
        });
        var savedCopy = Path.Combine(_root, "saved", "reload_ak.vnmclip_c");

        var edited = ClipExporter.Export(sandbox, "reload_ak", editedFile, toSandbox: true, extraOutputPath: savedCopy);

        Assert.False(edited.IdenticalToTemplate);
        // The elbow moves the hand, and the weapon's hand target follows the hand (HandGlue).
        Assert.Equal(["arm_lower_L", "wpnHand_L"], edited.ChangedBones["arms"].Order());
        Assert.False(edited.ChangedBones.ContainsKey("weapon"));
        Assert.Equal(File.ReadAllBytes(edited.SandboxOutput!), File.ReadAllBytes(savedCopy));
        var last = ClipExporter.LastExport(sandbox, "reload_ak");
        Assert.NotNull(last);
        Assert.Equal(edited.OutputSha256, last.OutputSha256);
        Assert.Equal(["arm_lower_L", "wpnHand_L"], last.ChangedBones["arms"].Order());
    }
}
