using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Globalization;
using System.Numerics;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Skeletons;

namespace WeaponLab.App;

/// <summary>
/// The outliner's Skeleton node: the bones the weapon's clips drive, as a
/// hierarchy. With a custom skeleton, new bones are marked ●, bones kept from
/// the slot's skeleton without mesh ○, and bones placed relative to the weapon
/// body ⚠. Selecting a bone shows its rest pose and which exports move it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The Property Browser shows a bone; status refreshes leave it alone.</summary>
    private bool _showingBone;

    /// <summary>Fills skeleton nodes the first time they open.</summary>
    private void WatchSkeletonNodes(IEnumerable<ExplorerNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Kind == ExplorerNodeKind.Skeleton)
            {
                node.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ExplorerNode.IsExpanded) && node.IsExpanded)
                    {
                        FillSkeletonNode(node);
                    }
                };
            }

            WatchSkeletonNodes(node.Children);
        }
    }

    private void FillSkeletonNode(ExplorerNode node)
    {
        if (_connection is not { } connection || node.Weapon is not { } weapon
            || node.Children.Count != 1 || node.Children[0].BoneName is not null)
        {
            return; // Already filled.
        }

        try
        {
            var (skeleton, setup) = SkeletonOf(connection, weapon);
            node.Children.Clear();
            if (skeleton is null)
            {
                node.Detail = "no weapon skeleton";
                return;
            }

            foreach (var root in BoneNodes(weapon, skeleton, setup))
            {
                node.Children.Add(root);
            }

            node.Detail = setup is null
                ? $"{Path.GetFileName(skeleton.ResourcePath)} ({skeleton.Bones.Count} bones)"
                : $"{Path.GetFileName(skeleton.ResourcePath)} ({skeleton.Bones.Count} bones, {setup.ExtraBones.Count} new)";
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            node.Children.Clear();
            node.Detail = "could not be read: " + error.Message;
        }
    }

    /// <summary>The bone hierarchy as tree nodes, marked: ● new, ○ no mesh, ⚠ placed on the weapon body.</summary>
    private static List<ExplorerNode> BoneNodes(WeaponLab.Core.Weapons.WeaponEntry weapon, SkeletonInfo skeleton, CustomSkeletonSetup? setup)
    {
        var extra = setup?.ExtraBones.ToHashSet(StringComparer.Ordinal) ?? [];
        var noMesh = setup?.SkeletonOnlyBones.ToHashSet(StringComparer.Ordinal) ?? [];
        var corrected = setup?.Corrected.ToHashSet(StringComparer.Ordinal) ?? [];
        var roots = new List<ExplorerNode>();
        var made = new ExplorerNode[skeleton.Bones.Count];
        foreach (var bone in skeleton.Bones)
        {
            var (mark, detail) = corrected.Contains(bone.Name) ? ("⚠ ", "placed on the body")
                : extra.Contains(bone.Name) ? ("● ", "new")
                : noMesh.Contains(bone.Name) ? ("○ ", "no mesh") : ("", "");
            made[bone.Index] = new ExplorerNode
            {
                Kind = ExplorerNodeKind.Bone,
                Label = mark + bone.Name,
                Detail = detail,
                Weapon = weapon,
                BoneName = bone.Name,
                IsExpanded = bone.ParentIndex < 0 || bone.Name == "weapon_offset",
            };
            if (bone.ParentIndex < 0)
            {
                roots.Add(made[bone.Index]);
            }
            else
            {
                made[bone.ParentIndex].Children.Add(made[bone.Index]);
            }
        }

        return roots;
    }

    /// <summary>
    /// The outliner colour of a weapon's Skeleton node: plain for the slot's own
    /// skeleton, yellow while a custom one is not in the addon yet, green once
    /// it is (an addon's own skeleton is green from the start).
    /// </summary>
    private (WeaponState State, string Tip) SkeletonState(WeaponLab.Core.Weapons.WeaponEntry weapon, IReadOnlyList<string> reasons)
    {
        var setup = _sandboxes.Exists(weapon.Id) ? CustomSkeletons.Load(_sandboxes.Open(weapon.Id)) : null;
        if (setup is null)
        {
            return (WeaponState.Untouched, "");
        }

        var pending = reasons.Where(reason => reason.Contains("skeleton", StringComparison.OrdinalIgnoreCase)).ToList();
        return pending.Count > 0
            ? (WeaponState.NeedsApply, "Not in the addon yet:\n" + string.Join("\n", pending))
            : (WeaponState.Applied, setup.FromAddon ? $"The addon's own skeleton {Path.GetFileName(setup.Path)}." : $"Custom skeleton {Path.GetFileName(setup.Path)}; the addon has it.");
    }

    /// <summary>The skeleton the weapon's clips drive in its sandbox (a custom one), else the slot's own.</summary>
    private (SkeletonInfo? Skeleton, CustomSkeletonSetup? Setup) SkeletonOf(WeaponLab.Core.Game.GameConnection connection, WeaponLab.Core.Weapons.WeaponEntry weapon)
    {
        if (_sandboxes.Exists(weapon.Id) && _sandboxes.Open(weapon.Id) is { } sandbox && CustomSkeletons.Load(sandbox) is { } setup)
        {
            return (CustomSkeletons.ReadSkeleton(sandbox, setup), setup);
        }

        var own = weapon.ClipPaths.Count == 0 ? null : connection.ReadClip(weapon.ClipPaths[0]).SecondarySkeletons.FirstOrDefault();
        return (own is null ? null : connection.ReadSkeleton(own), null);
    }

    /// <summary>The weapon skeleton's bones besides its root (the props the Build .blend window offers).</summary>
    private IReadOnlyList<string> WeaponBones(WeaponLab.Core.Game.GameConnection connection, WeaponLab.Core.Weapons.WeaponEntry weapon)
    {
        try
        {
            var (skeleton, _) = SkeletonOf(connection, weapon);
            return skeleton?.Bones.Where(bone => bone.ParentIndex >= 0).Select(bone => bone.Name).ToList() ?? [];
        }
        catch (Exception error) when (error is IOException or InvalidDataException or KeyNotFoundException)
        {
            return [];
        }
    }

    /// <summary>
    /// The model check (0.23) for a weapon on a custom skeleton: does the model it shows in game use that
    /// skeleton, with every shared bone under the same parent? Null without a custom skeleton.
    /// </summary>
    private ModelCheckResult? ModelCheckFor(WeaponLab.Core.Weapons.WeaponEntry weapon)
    {
        if (_connection is not { } connection || !_sandboxes.Exists(weapon.Id))
        {
            return null;
        }

        try
        {
            var sandbox = _sandboxes.Open(weapon.Id);
            if (CustomSkeletons.Load(sandbox) is not { } setup)
            {
                return null;
            }

            return ModelCheck.For(connection, Target(), _profile, BlocksFor(weapon), CustomSkeletons.ReadSkeleton(sandbox, setup), setup.SourceModel);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or KeyNotFoundException)
        {
            return null;
        }
    }

    private IReadOnlyList<string> ModelProblems(WeaponLab.Core.Weapons.WeaponEntry weapon) => ModelCheckFor(weapon)?.Problems() ?? [];

    /// <summary>The bone's parent in the model the weapon shows in game, against the skeleton (custom skeletons only).</summary>
    private PropertyRow ModelParentRow(WeaponLab.Core.Weapons.WeaponEntry weapon, string boneName)
    {
        if (ModelCheckFor(weapon) is not { Found: true } check)
        {
            return new PropertyRow("In the model", "not checked (CS2's skeleton, or the model was not found)");
        }

        var model = Path.GetFileName(check.Model);
        return check.Mismatches.FirstOrDefault(item => item.Bone == boneName) is { } mismatch
            ? new PropertyRow("In the model", $"⚠ under {mismatch.ModelParent ?? "nothing (a root bone)"} in {model}, not {mismatch.SkeletonParent}: parent it like the skeleton (see the Skeleton row)")
            : new PropertyRow("In the model", $"no parent mismatch in {model}");
    }

    /// <summary>Property Browser for a bone: parent, rest pose, how it got into the skeleton, and exports that move it.</summary>
    private IReadOnlyList<PropertyGroup>? BoneGroups(WeaponLab.Core.Weapons.WeaponEntry weapon, string boneName)
    {
        if (_connection is not { } connection)
        {
            return null;
        }

        var (skeleton, setup) = SkeletonOf(connection, weapon);
        var bone = skeleton?.Bones.FirstOrDefault(item => item.Name == boneName);
        if (skeleton is null || bone is null)
        {
            return null;
        }

        string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        var rest = bone.Rest;
        var angles = EulerDegrees(rest.Rotation);
        var origin = setup is null ? "CS2's default skeleton for this slot"
            : setup.Corrected.Contains(boneName) ? "the model, placed relative to the weapon body (its parent rests differently in the model)"
            : setup.ExtraBones.Contains(boneName) ? $"the model ({Path.GetFileNameWithoutExtension(setup.ModelCopy)})"
            : setup.SkeletonOnlyBones.Contains(boneName) ? "the slot's skeleton (the model has no mesh on it)"
            : "the slot's skeleton";
        var movedBy = new List<string>();
        if (_sandboxes.Exists(weapon.Id))
        {
            var sandbox = _sandboxes.Open(weapon.Id);
            foreach (var clip in sandbox.Manifest.Clips)
            {
                var clipId = WeaponLab.Core.Blender.RigPackage.ClipId(clip.ResourcePath);
                if (ClipExporter.LastExport(sandbox, clipId) is { IdenticalToTemplate: false } report
                    && report.ChangedBones.TryGetValue("weapon", out var bones) && bones.Contains(boneName))
                {
                    movedBy.Add(clipId);
                }
            }
        }

        var settings = _profile.Weapon(weapon.Id);
        var ticked = settings.PropBones.Contains(boneName);
        var underProp = ticked && !WeaponLab.Core.Blender.RigPackage.PropBones(skeleton, settings.PropBones).Contains(boneName);
        var propRow = bone.ParentIndex < 0
            ? new PropertyRow("Prop", "The root carries the whole gun; it cannot be kept apart from it.")
            : new PropertyRow("Prop", "Keep apart from the gun: holds still while the gun moves (e.g. a spare magazine held in the other hand). Build .blend again to apply."
                + (underProp ? " It moves with a ticked bone above it." : ""))
            {
                IsChecked = ticked,
                Toggled = value => SetPropBone(weapon, boneName, value),
            };

        return
        [
            new PropertyGroup("Bone",
            [
                new PropertyRow("Name", bone.Name),
                new PropertyRow("Parent", bone.ParentIndex < 0 ? "(root)" : skeleton.Bones[bone.ParentIndex].Name),
                new PropertyRow("Index", bone.Index.ToString(CultureInfo.InvariantCulture)),
                new PropertyRow("Skeleton", skeleton.ResourcePath),
                new PropertyRow("From", origin),
                ModelParentRow(weapon, boneName),
                propRow,
            ]),
            new PropertyGroup("Rest pose (parent space)",
            [
                new PropertyRow("Position", $"{F(rest.Translation.X)}, {F(rest.Translation.Y)}, {F(rest.Translation.Z)}"),
                new PropertyRow("Rotation (°)", $"{F(angles.X)}, {F(angles.Y)}, {F(angles.Z)}"),
                new PropertyRow("Scale", F(rest.Scale)),
            ]),
            new PropertyGroup("Edits", [new PropertyRow("Exports that move it", movedBy.Count == 0 ? "none" : string.Join(", ", movedBy))]),
        ];
    }

    /// <summary>Ticks or unticks a bone's "Keep apart from the gun" (the profile's PropBones); the next Build .blend applies it.</summary>
    private void SetPropBone(WeaponLab.Core.Weapons.WeaponEntry weapon, string boneName, bool keepApart)
    {
        var props = _profile.EnsureWeapon(weapon.Id).PropBones;
        if (!(keepApart ? props.Add(boneName) : props.Remove(boneName)))
        {
            return;
        }

        SaveProfile();
        Log(keepApart
            ? $"{weapon.Id}: {boneName} is kept apart from the gun. Build .blend again to apply."
            : $"{weapon.Id}: {boneName} moves with the gun again. Build .blend again to apply.");
        ShowBoneProperties(weapon, boneName);
    }

    /// <summary>A Property Browser checkbox (<see cref="PropertyRow.Toggled"/>); ignores the state the row was drawn with.</summary>
    private void OnPropertyToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: PropertyRow { Toggled: { } toggled } row } box && (box.IsChecked == true) != row.IsChecked)
        {
            toggled(box.IsChecked == true);
        }
    }

    private void ShowBoneProperties(WeaponLab.Core.Weapons.WeaponEntry weapon, string boneName)
    {
        if (BoneGroups(weapon, boneName) is { } groups)
        {
            _showingBone = true;
            ShowProperties(groups);
        }
    }

    /// <summary>Pitch, yaw, roll in degrees (Source order: Rz · Ry · Rx).</summary>
    private static Vector3 EulerDegrees(Quaternion rotation)
    {
        var m = Matrix4x4.CreateFromQuaternion(rotation);
        var pitch = MathF.Asin(Math.Clamp(-m.M13, -1, 1));
        var yaw = MathF.Atan2(m.M12, m.M11);
        var roll = MathF.Atan2(m.M23, m.M33);
        return new Vector3(pitch, yaw, roll) * (180 / MathF.PI);
    }
}
