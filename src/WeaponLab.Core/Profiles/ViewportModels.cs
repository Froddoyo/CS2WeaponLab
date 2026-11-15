using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Skeletons;

namespace WeaponLab.Core.Profiles;

/// <summary>
/// The model the viewport and Build .blend show for a weapon. Default (no
/// pick) is CS2's model for the slot (user feedback 2026-10-31: Default showed
/// the custom skeleton's model). A skeleton built from a model stores that
/// model as the pick, so the weapon still shows its own mesh.
/// </summary>
public static class ViewportModels
{
    /// <summary>
    /// Once per profile: weapons whose custom skeleton was built from a model and
    /// that have no pick keep showing that model, stored as their pick. Returns
    /// the weapons changed; true in <see cref="Profile.ViewportDefaultIsCs2"/> afterwards.
    /// </summary>
    public static IReadOnlyList<string> KeepOldDefaults(Profile profile, SandboxStore sandboxes)
    {
        if (profile.ViewportDefaultIsCs2)
        {
            return [];
        }

        var changed = new List<string>();
        foreach (var sandbox in sandboxes.List())
        {
            var weaponId = sandbox.Manifest.WeaponId;
            if (profile.Weapon(weaponId).PreviewModel is null && ModelOf(CustomSkeletons.Load(sandbox)) is { } model)
            {
                profile.EnsureWeapon(weaponId).PreviewModel = model;
                changed.Add(weaponId);
            }
        }

        profile.ViewportDefaultIsCs2 = true;
        return changed;
    }

    /// <summary>The model a custom skeleton was built from (a <see cref="Blender.PreviewModels"/> key), or null for a skeleton file.</summary>
    public static string? ModelOf(CustomSkeletonSetup? setup) =>
        setup is { SourceModel.Length: > 0 } && !setup.SourceModel.StartsWith(CustomSkeletons.FilePrefix, StringComparison.Ordinal) ? setup.SourceModel : null;
}
