using System.Numerics;
using SharpGLTF.Schema2;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Skeletons;

namespace WeaponLab.Core.Viewport;

/// <summary>One bone of the weapon skeleton in the viewport, in game units and model space (Z up).</summary>
/// <param name="Rest">The bone's model-space rest transform (row-vector convention, as System.Numerics).</param>
/// <param name="Extra">The bone is not in CS2's skeleton for the slot (a custom skeleton's new bone).</param>
/// <param name="Arms">The bone belongs to the viewmodel arms (drawn and toggled with them).</param>
public sealed record ViewportBone(string Name, int Parent, Matrix4x4 Rest, bool Extra, bool Arms = false);

/// <summary>
/// A mesh ready to draw: positions and normals already in the skeleton's
/// model space at rest, plus up to four skeleton bones and weights per vertex
/// for playing clips (see <see cref="SkinMatrices"/>).
/// </summary>
public sealed class ViewportMesh
{
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required uint[] Indices { get; init; }

    /// <summary>Four joint indices per vertex (into <see cref="JointBones"/>).</summary>
    public required int[] Bones { get; init; }

    /// <summary>For each joint of the mesh, the scene bone it follows.</summary>
    public required int[] JointBones { get; init; }

    /// <summary>
    /// For each joint, the inverse of its bind pose in game space. A vertex
    /// follows its joints by this × the bone's current transform, as the game
    /// (and the Blender build) skin it: a joint whose bind differs from the
    /// skeleton's rest (an agent's relaxed arm) snaps onto the animated bone.
    /// </summary>
    public required Matrix4x4[] JointInverseBinds { get; init; }

    /// <summary>Four weights per vertex, summing to 1.</summary>
    public required float[] Weights { get; init; }

    /// <summary>A sleeve or glove mesh (shown with the arms).</summary>
    public bool Arms { get; init; }

    /// <summary>For each joint, how the game turns it procedurally (a forearm twist bone), or null.</summary>
    public ViewportTwist?[] JointTwists { get; init; } = [];

    public int VertexCount => Positions.Length / 3;

    /// <summary>Each joint's skinning matrix for a pose of the scene's bones: its inverse bind × its current transform.</summary>
    public Matrix4x4[] SkinMatrices(Matrix4x4[] pose)
    {
        var skin = new Matrix4x4[JointBones.Length];
        for (var joint = 0; joint < skin.Length; joint++)
        {
            skin[joint] = joint < JointTwists.Length && JointTwists[joint] is { } twist
                ? twist.InverseBind * twist.Current(pose)
                : JointInverseBinds[joint] * pose[JointBones[joint]];
        }

        return skin;
    }
}

/// <summary>
/// A twist bone the rig lacks (agent arms' arm_lower_*_TWIST*). The game
/// turns it with a share of the child's roll about the bone, spreading a
/// wrist turn along the forearm; held rigid, the forearm pinches.
/// </summary>
/// <param name="Parent">The scene bone the twist bone hangs from (arm_lower).</param>
/// <param name="Child">The scene bone whose roll it shares (hand).</param>
/// <param name="Share">How much of the roll it takes: how far along the bone it sits (0..1).</param>
/// <param name="RestOffset">Its bind relative to the parent's bind.</param>
/// <param name="InverseBind">The inverse of its own bind in game space.</param>
/// <param name="ChildRestRelative">The inverse of the child's rest relative to the parent's rest.</param>
/// <param name="ChildAxis">The parent-to-child direction at rest in the child's own frame.</param>
public sealed record ViewportTwist(int Parent, int Child, float Share, Matrix4x4 RestOffset, Matrix4x4 InverseBind, Matrix4x4 ChildRestRelative, Vector3 ChildAxis)
{
    public Matrix4x4 Current(Matrix4x4[] pose)
    {
        var (parent, child) = (pose[Parent], pose[Child]);
        var rigid = RestOffset * parent;
        if (!Matrix4x4.Invert(parent, out var parentInverse))
        {
            return rigid;
        }

        // The child's turn since rest, in its own frame, split into swing and the twist about the bone.
        var delta = Quaternion.CreateFromRotationMatrix(child * parentInverse * ChildRestRelative);
        var angle = 2 * MathF.Atan2(Vector3.Dot(new Vector3(delta.X, delta.Y, delta.Z), ChildAxis), delta.W);
        angle = MathF.IEEERemainder(angle, 2 * MathF.PI);
        var axis = child.Translation - parent.Translation;
        if (axis.LengthSquared() < 1e-8f)
        {
            return rigid;
        }

        var at = rigid.Translation;
        var turn = Matrix4x4.CreateTranslation(-at) * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), Share * angle) * Matrix4x4.CreateTranslation(at);
        return rigid * turn;
    }
}

/// <param name="Skeleton">The skeleton's resource path.</param>
/// <param name="MeshSource">Which model the mesh came from (for the viewport's caption).</param>
public sealed record ViewportScene(string Skeleton, IReadOnlyList<ViewportBone> Bones, ViewportMesh? Mesh, string? MeshSource, IReadOnlyList<string> Warnings)
{
    /// <summary>The model-space rest transforms of every bone.</summary>
    public Matrix4x4[] RestPose() => Bones.Select(bone => bone.Rest).ToArray();

    /// <summary>The sleeve and glove meshes, weighted to the arm bones.</summary>
    public IReadOnlyList<ViewportMesh> ArmMeshes { get; init; } = [];

    /// <summary>How many of <see cref="Bones"/> are the viewmodel arms (they come first); 0 without arms.</summary>
    public int ArmBoneCount { get; init; }

    public bool HasArms => ArmBoneCount > 0;
}

/// <summary>
/// What the Animations tab's viewport shows for a weapon: the skeleton its
/// clips drive (the sandbox's custom skeleton when it has one, else CS2's for
/// the slot) and the weapon model picked for Blender (else CS2's), aligned the
/// way the Blender build aligns its preview: by the weapon's anchor bone.
/// </summary>
public static class ViewportScenes
{
    /// <summary>glTF is in metres; the game in units of 0.0254 m.</summary>
    private const float UnitsPerMetre = 1f / 0.0254f;

    private const string Anchor = "weapon";

    /// <param name="sandbox">The weapon's sandbox in the active profile, or null.</param>
    /// <param name="previewModel">A <see cref="PreviewModels"/> key from the weapon's Build .blend choice, or null for the default.</param>
    /// <param name="cacheFolder">Where exported meshes are kept between sessions.</param>
    /// <param name="arms">The sleeve and glove models on the arms (the Build .blend choice); null shows no arms.</param>
    public static ViewportScene Build(GameConnection connection, Weapons.WeaponEntry weapon, SandboxInfo? sandbox, string? previewModel, string cacheFolder, ArmsPreview? arms = null)
    {
        var warnings = new List<string>();
        var stockPath = weapon.ClipPaths.Count == 0 ? null : connection.ReadClip(weapon.ClipPaths[0]).SecondarySkeletons.FirstOrDefault();
        if (stockPath is null)
        {
            return new ViewportScene("", [], null, null, ["This weapon's clips have no weapon skeleton."]);
        }

        var stock = connection.ReadSkeleton(stockPath);
        var setup = sandbox is null ? null : CustomSkeletons.Load(sandbox);
        var skeleton = setup is null ? stock : SkeletonInfo.Read(CustomSkeletons.SkeletonBytes(sandbox!, setup), setup.Path);
        var stockNames = stock.Bones.Select(bone => bone.Name).ToHashSet(StringComparer.Ordinal);

        // The viewmodel arms come first; the weapon skeleton hangs off their wpn bone, as in game (and the Blender build).
        var bones = new List<ViewportBone>();
        var wpn = -1;
        if (arms is not null)
        {
            var armSkeleton = connection.ReadSkeleton(ClipInfo.ViewmodelSkeleton);
            var armSpace = SkeletonBuilder.ModelSpace(armSkeleton.Bones);
            bones.AddRange(armSkeleton.Bones.Select((bone, index) => new ViewportBone(bone.Name, bone.ParentIndex, Matrix(armSpace[index]), Extra: false, Arms: true)));
            wpn = bones.FindIndex(bone => bone.Name == WeaponMount);
            if (wpn < 0)
            {
                warnings.Add($"The arm skeleton has no {WeaponMount} bone; the arms are not shown.");
                bones.Clear();
            }
        }

        var armCount = bones.Count;
        var mount = wpn >= 0 ? bones[wpn].Rest : Matrix4x4.Identity;
        var modelSpace = SkeletonBuilder.ModelSpace(skeleton.Bones);
        bones.AddRange(skeleton.Bones.Select((bone, index) => new ViewportBone(
            bone.Name,
            bone.ParentIndex >= 0 ? bone.ParentIndex + armCount : wpn,
            Matrix(modelSpace[index]) * mount,
            !stockNames.Contains(bone.Name))));

        // The model: the weapon's pick, else CS2's for the slot (0.21; a skeleton built from a model stores that model as the pick).
        var key = previewModel;
        ViewportMesh? mesh = null;
        string? source = null;
        try
        {
            using var exporter = new PreviewExporter(connection.Install);
            var (gamePath, addon) = key is null ? (exporter.FindWeaponModel(stockPath), null) : Resolve(connection, key);
            var glb = Export(exporter, connection, key ?? gamePath, gamePath, addon, key is null ? "body_hd" : null, cacheFolder);
            source = glb is null ? null : key is null ? Path.GetFileNameWithoutExtension(gamePath) + " (CS2)" : Path.GetFileNameWithoutExtension(addon?.File ?? gamePath);
            mesh = glb is null ? null : LoadMesh(glb, bones, armCount, bones.Count - armCount, Anchor, warnings);
            if (glb is null)
            {
                warnings.Add("No model found for this skeleton; only the bones are shown.");
            }

            // The arms: sleeves and gloves, placed by the right hand and weighted to the arm bones.
            var armMeshes = new List<ViewportMesh>();
            if (armCount > 0)
            {
                foreach (var (part, filter) in new[] { (arms!.Sleeves, "firstperson_sleeves"), (arms.Gloves, "viewmodel") })
                {
                    if (string.IsNullOrEmpty(part))
                    {
                        continue; // "" means none.
                    }

                    var (partPath, partAddon) = part.StartsWith(PreviewModels.AddonPrefix, StringComparison.Ordinal) || part.StartsWith(PreviewModels.GamePrefix, StringComparison.Ordinal)
                        ? Resolve(connection, part)
                        : (part, null);
                    if (Export(exporter, connection, part, partPath, partAddon, filter, cacheFolder) is { } partGlb)
                    {
                        var partMesh = LoadMesh(partGlb, bones, 0, armCount, ArmAnchor, warnings);
                        armMeshes.Add(new ViewportMesh
                        {
                            Positions = partMesh.Positions, Normals = partMesh.Normals, Indices = partMesh.Indices, Bones = partMesh.Bones, Weights = partMesh.Weights,
                            JointBones = partMesh.JointBones, JointInverseBinds = partMesh.JointInverseBinds, JointTwists = partMesh.JointTwists, Arms = true,
                        });
                    }
                }
            }

            return new ViewportScene(skeleton.ResourcePath, bones, mesh, source, warnings) { ArmMeshes = armMeshes, ArmBoneCount = armCount };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            warnings.Add($"The model could not be shown: {error.Message}");
        }

        return new ViewportScene(skeleton.ResourcePath, bones, mesh, source, warnings) { ArmBoneCount = armCount };
    }

    /// <summary>The arm bone the weapon is mounted on.</summary>
    private const string WeaponMount = "wpn";

    /// <summary>Arm previews are placed by the right hand, as in the Blender build.</summary>
    private const string ArmAnchor = "hand_R";

    private static (string? GamePath, (AddonInfo Addon, string File)? Addon) Resolve(GameConnection connection, string key) =>
        (key.StartsWith(PreviewModels.GamePrefix, StringComparison.Ordinal) ? key[PreviewModels.GamePrefix.Length..] : null, PreviewModels.ResolveAddon(connection, key));

    /// <summary>A bone pose as a model-space matrix: scale, then rotation, then translation.</summary>
    public static Matrix4x4 Matrix(BonePose pose) =>
        Matrix4x4.CreateScale(pose.Scale) * Matrix4x4.CreateFromQuaternion(pose.Rotation) * Matrix4x4.CreateTranslation(pose.Translation);


    /// <summary>
    /// Exports (or reuses) a model's preview mesh and returns the .glb path,
    /// or null when there is no such model. Cached by what was exported and
    /// when its source last changed.
    /// </summary>
    private static string? Export(PreviewExporter exporter, GameConnection connection, string? cacheKey, string? gamePath, (AddonInfo Addon, string File)? addon, string? filter, string cacheFolder)
    {
        if (gamePath is null && addon is null)
        {
            return null;
        }

        var stamp = addon is { } found ? File.GetLastWriteTimeUtc(found.File).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : connection.Version?.PatchVersion ?? "";
        var name = GameArchive.Sha256(System.Text.Encoding.UTF8.GetBytes($"{cacheKey ?? gamePath}|{filter}|{stamp}"))[..20] + ".glb";
        var file = Path.Combine(cacheFolder, name);
        if (File.Exists(file))
        {
            return file;
        }

        // The exporter also writes a physics .glb beside the mesh: export into a fresh folder and keep only the mesh.
        var staging = Path.Combine(cacheFolder, "staging-" + Guid.NewGuid().ToString("N"));
        var exported = Path.Combine(staging, "mesh.glb");
        try
        {
            if (addon is { } model)
            {
                exporter.ExportAddonModel(model.File, model.Addon.GamePath, exported, filter);
            }
            else
            {
                exporter.Export(gamePath!, exported, filter);
            }

            File.Move(exported, file, overwrite: true);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }

        return file;
    }

    /// <summary>
    /// Reads a preview .glb into the scene's space: every vertex is moved from
    /// the glTF bind pose into game units by the anchor bone (its inverse bind
    /// matrix, the metre-to-unit scale, then the skeleton's rest anchor), and
    /// each glTF joint is mapped to the bone of the same name among
    /// <paramref name="count"/> bones from <paramref name="first"/>. That is how
    /// the game binds a model to an animation: an agent glove's arm_upper_L follows
    /// the viewmodel rig's arm_upper_L. A joint the rig lacks (a clavicle) rides
    /// with its nearest mapped descendant, else its nearest mapped ancestor; a
    /// twist bone also takes its share of the child's roll (<see cref="ViewportTwist"/>).
    /// </summary>
    internal static ViewportMesh LoadMesh(string glb, IReadOnlyList<ViewportBone> bones, int first, int count, string anchor, List<string> warnings)
    {
        var model = ModelRoot.Load(glb);
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = first; index < first + count; index++)
        {
            byName.TryAdd(bones[index].Name, index);
        }

        var anchorBone = byName.TryGetValue(anchor, out var anchorIndex) ? anchorIndex : first;
        var anchorNode = model.LogicalNodes.FirstOrDefault(node => string.Equals(NameOf(node), anchor, StringComparison.OrdinalIgnoreCase));
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<uint>();
        var vertexBones = new List<int>();
        var weights = new List<float>();
        var joints = new List<(int Bone, Matrix4x4 InverseBind)>();
        var twists = new List<ViewportTwist?>();

        foreach (var node in model.LogicalNodes.Where(node => node.Mesh is not null))
        {
            var skin = node.Skin;
            var toGame = Placement(node, skin, anchorNode, bones[anchorBone].Rest, anchor, out var frame);

            // This node's joints, appended to the mesh's: each follows a scene bone from its own bind pose.
            var firstJoint = joints.Count;
            if (skin is null)
            {
                // Unskinned: rides the anchor bone rigidly from where it was placed.
                joints.Add((anchorBone, Matrix4x4.Invert(bones[anchorBone].Rest, out var inverse) ? inverse : Matrix4x4.Identity));
                twists.Add(null);
            }
            else
            {
                for (var joint = 0; joint < skin.JointsCount; joint++)
                {
                    var (jointNode, inverseBind) = skin.GetJoint(joint);
                    var bone = BoneFor(jointNode, byName, anchorBone, out var matched);
                    var ridesWith = JointInverseBind(RidesWith(jointNode, inverseBind, matched), frame);
                    joints.Add((bone, ridesWith));
                    twists.Add(Twist(jointNode, matched, bone, byName, bones, JointInverseBind(inverseBind, frame), ridesWith));
                }
            }

            var jointBones = skin is null ? [] : Enumerable.Range(firstJoint, skin.JointsCount).ToArray();
            foreach (var primitive in node.Mesh!.Primitives)
            {
                var position = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                if (position is null || primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                {
                    continue;
                }

                var normal = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                var vertexJoints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
                var jointWeights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
                var start = (uint)(positions.Count / 3);
                for (var vertex = 0; vertex < position.Count; vertex++)
                {
                    var placed = Vector3.Transform(position[vertex], toGame);
                    var turned = normal is null ? Vector3.UnitZ : Vector3.Normalize(Vector3.TransformNormal(normal[vertex], toGame));
                    positions.AddRange([placed.X, placed.Y, placed.Z]);
                    normals.AddRange([turned.X, turned.Y, turned.Z]);
                    if (vertexJoints is not null && jointWeights is not null && jointBones.Length > 0)
                    {
                        var (j, w) = (vertexJoints[vertex], jointWeights[vertex]);
                        float[] four = [w.X, w.Y, w.Z, w.W];
                        int[] boneOf = [jointBones[(int)j.X], jointBones[(int)j.Y], jointBones[(int)j.Z], jointBones[(int)j.W]];
                        var total = four.Sum();
                        for (var slot = 0; slot < 4; slot++)
                        {
                            vertexBones.Add(boneOf[slot]);
                            weights.Add(total > 0 ? four[slot] / total : slot == 0 ? 1 : 0);
                        }
                    }
                    else
                    {
                        vertexBones.AddRange([firstJoint, firstJoint, firstJoint, firstJoint]);
                        weights.AddRange([1, 0, 0, 0]);
                    }
                }

                indices.AddRange(primitive.GetIndices().Select(index => start + index));
            }
        }

        if (positions.Count == 0)
        {
            warnings.Add($"{Path.GetFileName(glb)} has no triangles to show.");
        }

        return new ViewportMesh
        {
            Positions = [.. positions],
            Normals = [.. normals],
            Indices = [.. indices],
            Bones = [.. vertexBones],
            Weights = [.. weights],
            JointBones = [.. joints.Select(joint => joint.Bone)],
            JointInverseBinds = [.. joints.Select(joint => joint.InverseBind)],
            JointTwists = [.. twists],
        };
    }

    /// <summary>
    /// Where a glTF mesh node's vertices go in game space. Skinned: by the
    /// anchor joint's inverse bind matrix, then metres to units, then the
    /// skeleton's rest anchor. Unskinned: through the anchor node's world
    /// matrix the same way.
    /// </summary>
    /// <param name="frame">How the placement goes: glTF world to the anchor's frame (metres), then the matrix after the metre-to-unit scale.</param>
    private static Matrix4x4 Placement(Node node, Skin? skin, Node? anchorNode, Matrix4x4 restAnchor, string anchor, out (Matrix4x4 ToAnchor, Matrix4x4 After) frame)
    {
        var scale = Matrix4x4.CreateScale(UnitsPerMetre);
        if (skin is not null)
        {
            for (var joint = 0; joint < skin.JointsCount; joint++)
            {
                var (jointNode, inverseBind) = skin.GetJoint(joint);
                if (string.Equals(NameOf(jointNode), anchor, StringComparison.OrdinalIgnoreCase))
                {
                    frame = (inverseBind, restAnchor);
                    return inverseBind * scale * restAnchor;
                }
            }
        }

        if (anchorNode is not null && Matrix4x4.Invert(anchorNode.WorldMatrix, out var toAnchor))
        {
            frame = (toAnchor, restAnchor);
            return node.WorldMatrix * toAnchor * scale * restAnchor;
        }

        // No anchor at all: the glTF scene as it is, metres to units, Y up to Z up.
        frame = (Matrix4x4.Identity, YUpToZUp);
        return node.WorldMatrix * scale * YUpToZUp;
    }

    /// <summary>
    /// A joint's bind pose in game space, inverted: its glTF bind (the inverse
    /// of its inverse bind matrix) taken into the anchor's frame, its offset
    /// scaled from metres to units (the rotation is not scaled), then placed
    /// like the vertices.
    /// </summary>
    private static Matrix4x4 JointInverseBind(Matrix4x4 inverseBind, (Matrix4x4 ToAnchor, Matrix4x4 After) frame)
    {
        if (!Matrix4x4.Invert(inverseBind, out var bind) || !Matrix4x4.Decompose(bind * frame.ToAnchor, out _, out var rotation, out var offset))
        {
            return Matrix4x4.Identity;
        }

        var game = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(offset * UnitsPerMetre) * frame.After;
        return Matrix4x4.Invert(game, out var inverse) ? inverse : Matrix4x4.Identity;
    }

    /// <summary>
    /// The inverse bind a joint skins with. A joint the rig lacks (a forearm
    /// twist bone) rides rigidly with the bone it was mapped to, so it takes
    /// that bone's bind: its own bind moved by the rest offset between the
    /// two nodes. Keeping its own bind would pull its vertices along by that
    /// offset (the twist sits halfway down the forearm).
    /// </summary>
    private static Matrix4x4 RidesWith(Node joint, Matrix4x4 inverseBind, Node? matched)
    {
        if (matched is null || matched == joint
            || !Matrix4x4.Invert(inverseBind, out var bind)
            || !Matrix4x4.Invert(joint.WorldMatrix, out var jointWorldInverse))
        {
            return inverseBind;
        }

        var matchedBind = matched.WorldMatrix * jointWorldInverse * bind;
        return Matrix4x4.Invert(matchedBind, out var inverse) ? inverse : inverseBind;
    }

    /// <summary>
    /// The procedural turn of a twist joint the rig lacks (name containing
    /// "twist", riding its parent), sharing the roll of the parent's child that
    /// the rig has (arm_lower_L_TWIST1 between arm_lower_L and hand_L), or null.
    /// </summary>
    private static ViewportTwist? Twist(Node joint, Node? matched, int parentBone, IReadOnlyDictionary<string, int> byName,
        IReadOnlyList<ViewportBone> bones, Matrix4x4 ownInverseBind, Matrix4x4 parentInverseBind)
    {
        if (matched is null || matched == joint || matched != joint.VisualParent
            || joint.Name?.Contains("twist", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        var childNode = matched.VisualChildren.FirstOrDefault(node => node != joint && byName.ContainsKey(NameOf(node)));
        if (childNode is null)
        {
            return null;
        }

        // Its share of the roll: how far along parent → child it sits in the glTF.
        var (from, to, at) = (matched.WorldMatrix.Translation, childNode.WorldMatrix.Translation, joint.WorldMatrix.Translation);
        var length = (to - from).LengthSquared();
        var share = length < 1e-10f ? 0 : Math.Clamp(Vector3.Dot(at - from, to - from) / length, 0, 1);
        var childBone = byName[NameOf(childNode)];
        var (parentRest, childRest) = (bones[parentBone].Rest, bones[childBone].Rest);
        if (share < 0.05f || !Matrix4x4.Invert(ownInverseBind, out var bind) || !Matrix4x4.Invert(parentRest, out var parentRestInverse)
            || !Matrix4x4.Invert(childRest * parentRestInverse, out var childRestRelative) || !Matrix4x4.Invert(childRest, out var childRestInverse))
        {
            return null;
        }

        var axis = Vector3.TransformNormal(childRest.Translation - parentRest.Translation, childRestInverse);
        return axis.LengthSquared() < 1e-8f ? null
            : new ViewportTwist(parentBone, childBone, share, bind * parentInverseBind, ownInverseBind, childRestRelative, Vector3.Normalize(axis));
    }

    private static string NameOf(Node node) => node.Name ?? "";

    private static readonly Matrix4x4 YUpToZUp = Matrix4x4.CreateRotationX(MathF.PI / 2);

    /// <param name="matched">The glTF node whose name the bone has (the joint itself when it maps directly), or null for the fallback.</param>
    private static int BoneFor(Node joint, IReadOnlyDictionary<string, int> byName, int fallback, out Node? matched)
    {
        matched = joint;
        if (byName.TryGetValue(NameOf(joint), out var exact))
        {
            return exact;
        }

        // Nearest mapped descendant (breadth first): a sleeve weighted to the clavicle rides with the upper arm.
        var queue = new Queue<Node>(joint.VisualChildren);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (byName.TryGetValue(NameOf(node), out var below))
            {
                matched = node;
                return below;
            }

            foreach (var child in node.VisualChildren)
            {
                queue.Enqueue(child);
            }
        }

        for (var node = joint.VisualParent; node is not null; node = node.VisualParent)
        {
            if (byName.TryGetValue(NameOf(node), out var above))
            {
                matched = node;
                return above;
            }
        }

        matched = null;
        return fallback;
    }
}
