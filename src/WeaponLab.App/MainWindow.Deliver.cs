using Avalonia.Controls;
using Avalonia.Interactivity;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.VData;

namespace WeaponLab.App;

/// <summary>The Deliver tab: review and apply the profile to the target addon, undo, history.</summary>
public sealed partial class MainWindow
{
    private DeliveryPlan? _reviewedPlan;

    // Weapons left out of the push with nothing in it are hidden unless "Show left out" is ticked (user, 2026-11-07).
    private bool _showLeftOut;
    private int _leftOutCount;
    private bool _delivering;

    private void RefreshDeliverView()
    {
        var target = Target();
        DeliverTitle.Text = target is null ? "Connect, then pick a target addon in the status bar" : $"Profile '{_profile.Name}' → {target.Name}";
        ReviewDeliveryButton.IsEnabled = target is not null && !_delivering;
        // Avalonia reads "_" as an access-key marker; "__" shows a literal underscore.
        DeliverButton.Content = target is null ? "Push to addon" : $"Push to {target.Name.Replace("_", "__")}";
        DeliverButton.IsEnabled = target is not null && _reviewedPlan is { IsEmpty: false } && _reviewedPlan.Target.Name == target.Name && !_delivering;
        UndoDeliveryButton.IsEnabled = target is not null && !_delivering && AddonDelivery.LastDelivery(_sandboxes.WorkspacePath, target.Name) is not null;
        DeliveryHistoryList.ItemsSource = AddonDelivery.History(_sandboxes.WorkspacePath).Select(DeliveryRow.From).ToList();
        ReviewDeliveryButton.Classes.Set("suggested", ReviewDeliveryButton.IsEnabled && !DeliverButton.IsEnabled && HasPendingWeapons());
        DeliverButton.Classes.Set("suggested", DeliverButton.IsEnabled);

        if (_reviewedPlan is null && PlanList.ItemsSource is null)
        {
            PlanList.ItemsSource = PendingRows();
        }
    }

    private bool HasPendingWeapons() =>
        _connection?.Weapons.Any(weapon => StatusFor(weapon).State == WeaponState.NeedsApply) == true;

    /// <summary>Before a review: which weapons are yellow and why.</summary>
    private List<PlanRow> PendingRows()
    {
        if (_connection is null)
        {
            return [];
        }

        var rows = _connection.Weapons
            .Select(weapon => (Weapon: weapon, Status: StatusFor(weapon)))
            .Where(item => item.Status.State == WeaponState.NeedsApply)
            .Select(item => new PlanRow("pending", $"{item.Weapon.Id}: {string.Join(" ", item.Status.Reasons.Take(3))}", true) { Target = new WeaponLab.Core.Checks.CheckTarget(item.Weapon.Id) })
            .ToList();
        if (rows.Count == 0)
        {
            rows.Add(new PlanRow("status", Target() is null ? "Pick a target addon." : "Nothing pending: the addon has all of this profile's work.", false));
        }

        return rows;
    }

    private async void OnReviewDeliveryClicked(object? sender, RoutedEventArgs e) => await ReviewDeliveryAsync();

    /// <summary>Plans the apply and shows it in the list and the tree.</summary>
    private async Task ReviewDeliveryAsync()
    {
        if (_connection is not { } connection || Target() is not { } target)
        {
            return;
        }

        SetStatus($"Planning: profile '{_profile.Name}' → {target.Name}…");
        try
        {
            var plan = await Task.Run(() => DeliveryPlanner.Plan(_sandboxes, connection, target, _profile));
            _reviewedPlan = plan;
            PlanList.ItemsSource = PlanRows(plan);
            ShowPlanTree(plan);
            Log(plan.IsEmpty
                ? $"{target.Name} already matches profile '{_profile.Name}'."
                : $"Reviewed: {plan.Clips.Count} clip(s), {plan.Graphs.Count} graph(s), {plan.VDataEdits} vdata value(s), {plan.VDataResets} reset(s), {plan.Removals.Count} removal(s).");
        }
        catch (Exception error) when (error is DeliveryException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _reviewedPlan = null;
            ShowPlanError(error.Message);
            Log($"Cannot plan the apply: {error.Message}");
        }

        RefreshDeliverView();
    }

    /// <summary>The skeleton a warning names (the model check's fix), for Copy asset path; null when it names none.</summary>
    private static string? SkeletonPathIn(string message) =>
        System.Text.RegularExpressions.Regex.Match(message, @"animation/skeletons/[^\s,;:()""]+?\.vnmskel(?!_c)") is { Success: true } match ? match.Value : null;

    /// <summary>The plan as the tree, and the "Show left out (n)" toggle for weapons hidden from it.</summary>
    private void ShowPlanTree(DeliveryPlan plan)
    {
        DeliveryTree.ItemsSource = PlanTree(plan);
        DeliverShowLeftOut.Content = $"Show left out ({_leftOutCount})";
        DeliverShowLeftOut.IsVisible = _leftOutCount > 0;
    }

    private void OnDeliverShowLeftOutChanged(object? sender, RoutedEventArgs e)
    {
        if (DeliverShowLeftOut is null)
        {
            return; // Called while the window is still being built.
        }

        _showLeftOut = DeliverShowLeftOut.IsChecked == true;
        if (_reviewedPlan is { } plan)
        {
            ShowPlanTree(plan);
        }
    }

    /// <summary>A planning or push error in both views, with a Show button when it names a graph node, clip or weapon.</summary>
    private void ShowPlanError(string message)
    {
        var target = CheckTargetOf(message);
        PlanList.ItemsSource = new[] { new PlanRow("error", message, true) { Target = target } };
        DeliveryTree.ItemsSource = new[]
        {
            new DeliveryTreeNode { Label = "Cannot push", Detail = message, Action = DeliveryAction.Warning, Problem = message, Target = target },
        };
    }

    // ---- Tree view ----

    private void OnDeliverViewChanged(object? sender, RoutedEventArgs? e)
    {
        if (DeliveryTree is null || PlanList is null || PlanHeader is null || DeliverLegend is null)
        {
            return; // Called while the window is still being built.
        }

        var tree = DeliverTreeRadio.IsChecked == true;
        DeliveryTree.IsVisible = tree;
        DeliverLegend.IsVisible = tree;
        PlanList.IsVisible = !tree;
        PlanHeader.IsVisible = !tree;
        if (tree && DeliverLegend.Children.Count == 0)
        {
            foreach (var (action, meaning) in DeliveryTreeNode.Legend)
            {
                DeliverLegend.Children.Add(new Border
                {
                    Background = DeliveryTreeNode.BrushFor(action),
                    CornerRadius = new Avalonia.CornerRadius(2),
                    Padding = new Avalonia.Thickness(4, 0),
                    Margin = new Avalonia.Thickness(0, 0, 6, 2),
                    Child = new TextBlock { Text = meaning, FontSize = 10, Foreground = Avalonia.Media.Brushes.Black },
                });
            }
        }
    }

    /// <summary>
    /// The plan as a tree by weapon slot: each weapon the delivery touches,
    /// collapsed to a summary, opens to its animations, graphs, vdata values
    /// (addon now → new), skeleton and warnings. Files that belong to no single
    /// weapon (the root graph, the vdata compile) sit under Shared; the root
    /// graph opens to the weapons it routes.
    /// </summary>
    private List<DeliveryTreeNode> PlanTree(DeliveryPlan plan)
    {
        var connection = _connection!;
        _leftOutCount = 0;
        var previous = AddonManifest.Read(plan.Target);
        var files = plan.Files.Where(file => file.ManifestKey != AddonManifest.FileName).ToDictionary(file => file.ManifestKey);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AddonManifest.FileName };

        DeliveryAction FileAction(DeliveryFile file)
        {
            if (file.Restores)
            {
                return DeliveryAction.Restores;
            }

            if (!File.Exists(file.Destination))
            {
                return DeliveryAction.New;
            }

            var ours = previous?.Files.ContainsKey(file.ManifestKey) == true;
            return ours ? DeliveryAction.Updates : file.ManifestKey.EndsWith(".vnmgraph_c", StringComparison.Ordinal) ? DeliveryAction.Patched : DeliveryAction.Replaces;
        }

        DeliveryTreeNode FileNode(DeliveryFile file, string label)
        {
            placed.Add(file.ManifestKey);
            return new DeliveryTreeNode { Label = label, Detail = file.ManifestKey, Action = FileAction(file), AssetPath = WithoutCompiledSuffix(file.ManifestKey) };
        }

        // Which weapon a vdata block, graph or file belongs to.
        var blockOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var weapon in connection.Weapons)
        {
            foreach (var block in BlocksFor(weapon))
            {
                blockOwner.TryAdd(block, weapon.Id);
            }
        }

        VDataFile? addonVData = null;
        try
        {
            addonVData = File.Exists(plan.Target.VDataCompiledPath) ? VDataFile.Read(File.ReadAllBytes(plan.Target.VDataCompiledPath), plan.Target.VDataCompiledPath) : null;
        }
        catch (InvalidDataException)
        {
            // Shown without the addon's current values.
        }

        var stock = connection.ReadVData();
        var weaponIds = plan.Clips.Select(clip => clip.WeaponId)
            // The root graph plays a few clips itself (C4, Healthshot); it belongs to no weapon, so it lists none here.
            .Concat(plan.Graphs.Where(graph => graph != GraphTree.CustomParent).SelectMany(graph => connection.Weapons.Where(weapon => OwnsGraph(weapon.Id, graph)).Select(weapon => weapon.Id)))
            .Concat(plan.VDataValues.Select(value => blockOwner.GetValueOrDefault(value.Block)).OfType<string>())
            .Concat(plan.Swaps.Select(swap => blockOwner.GetValueOrDefault(swap.Block)).OfType<string>())
            .Concat(CommittableWeapons())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var weaponNodes = new List<DeliveryTreeNode>();
        foreach (var weaponId in weaponIds)
        {
            var groups = new List<DeliveryTreeNode>();
            var summary = new List<string>();

            var clips = ClipRows(weaponId, plan, files, FileNode);
            if (clips.Count > 0)
            {
                groups.Add(new DeliveryTreeNode { Label = "Animations", Detail = $"{clips.Count} clip(s)", Children = clips });
                summary.Add($"{clips.Count} clip(s)");
            }

            var graphs = plan.Graphs.Where(graph => graph != GraphTree.CustomParent && OwnsGraph(weaponId, graph) && files.ContainsKey(graph + "_c"))
                .Select(graph => FileNode(files[graph + "_c"], Path.GetFileName(graph))).ToList();
            if (graphs.Count > 0)
            {
                groups.Add(new DeliveryTreeNode { Label = "Graphs", Detail = $"{graphs.Count} graph(s)", Children = graphs });
                summary.Add($"{graphs.Count} graph(s)");
            }

            var vdataAction = files.TryGetValue(AddonManifest.VDataSourceKey, out var vdataFile) ? FileAction(vdataFile) : DeliveryAction.Updates;
            var values = plan.VDataValues.Where(value => blockOwner.GetValueOrDefault(value.Block) == weaponId)
                .Select(value =>
                {
                    var now = addonVData?[value.Block]?.Value(value.Path) ?? stock[value.Block]?.Value(value.Path) ?? "not set";
                    return new DeliveryTreeNode { Label = $"{value.Block}  {VDataGlossary.Describe(value.Path).Name}", Detail = $"{now} → {value.Value}   ({value.Path})", Action = vdataAction };
                })
                .Concat(plan.Swaps.Where(swap => blockOwner.GetValueOrDefault(swap.Block) == weaponId)
                    .Select(swap => new DeliveryTreeNode { Label = $"{swap.Block} based on {swap.Donor}", Detail = string.Join(", ", swap.Keys), Action = vdataAction }))
                .ToList();
            if (values.Count > 0)
            {
                groups.Add(new DeliveryTreeNode { Label = "VData", Detail = $"{values.Count} value(s)", Children = values });
                summary.Add($"{values.Count} vdata value(s)");
            }

            // A custom skeleton's file (versions before 0.23 also shipped a model copy in models/weapons/weaponlab/<weapon>/).
            var skeletonPath = _profile.Weapon(weaponId).CustomSkeleton?.Path;
            var skeleton = files.Values
                .Where(file => !placed.Contains(file.ManifestKey)
                    && ((skeletonPath is not null && file.ManifestKey == skeletonPath + "_c") || file.ManifestKey.Contains($"/weaponlab/{weaponId}/", StringComparison.OrdinalIgnoreCase)))
                .Select(file => FileNode(file, Path.GetFileName(file.ManifestKey))).ToList();
            if (skeleton.Count > 0)
            {
                groups.Add(new DeliveryTreeNode { Label = "Skeleton", Detail = "the custom skeleton (point your model at it in ModelDoc)", Children = skeleton });
                summary.Add("custom skeleton");
            }

            var removed = plan.Removals.Select(path => AddonManifest.KeyFor(plan.Target, path))
                .Where(key => key.Contains($"/{weaponId}/", StringComparison.OrdinalIgnoreCase) && placed.Add(key))
                .Select(key => new DeliveryTreeNode { Label = Path.GetFileName(key), Detail = key, Action = DeliveryAction.Removes, AssetPath = WithoutCompiledSuffix(key) }).ToList();
            if (removed.Count > 0)
            {
                groups.Add(new DeliveryTreeNode { Label = "Removed", Detail = $"{removed.Count} file(s)", Action = DeliveryAction.Removes, Children = removed });
                summary.Add($"{removed.Count} removed");
            }

            var blocks = BlocksFor(connection.FindWeapon(weaponId)!);
            var warnings = plan.Warnings.Where(warning => warning.Subject == weaponId || blocks.Contains(warning.Subject))
                .Select(warning => new DeliveryTreeNode { Label = warning.Subject, Detail = warning.Message, Action = DeliveryAction.Warning, Target = CheckTargetOf(warning.Message, warning.Subject), AssetPath = SkeletonPathIn(warning.Message) }).ToList();
            if (warnings.Count > 0)
            {
                groups.Insert(0, new DeliveryTreeNode { Label = "Warnings", Action = DeliveryAction.Warning, Children = warnings });
            }

            var committed = _profile.Weapon(weaponId).Animations == AnimationMode.Custom;
            if (!committed && summary.Count == 0 && warnings.Count == 0)
            {
                // Left out and nothing of it in this push: hidden unless "Show left out" is ticked (user, 2026-11-07).
                _leftOutCount++;
                if (!_showLeftOut)
                {
                    continue;
                }
            }

            var problem = committed ? null : WeaponProblem(weaponId, plan);
            weaponNodes.Add(new DeliveryTreeNode
            {
                Label = $"{(warnings.Count > 0 ? "⚠ " : "")}{WeaponLab.Core.Blender.WeaponLabels.For(weaponId)}  ({weaponId})",
                Detail = !committed ? "left out of this push" + (summary.Count == 0 ? "" : ": " + string.Join(" · ", summary)) : summary.Count == 0 ? "no file changes" : string.Join(" · ", summary),
                WeaponId = weaponId,
                CanToggle = true,
                Included = committed,
                Toggled = value => CommitAsync(() => _profile.EnsureWeapon(weaponId).Animations = value ? AnimationMode.Custom : AnimationMode.Default),
                Problem = problem,
                IsExpanded = warnings.Count > 0,
                Children = groups,
            });
        }

        // ---- Shared: the root graph (opening to the weapons it routes), other graphs, vdata, the rest ----
        var shared = new List<DeliveryTreeNode>();
        if (files.TryGetValue(GraphTree.CustomParent + "_c", out var root))
        {
            var routed = RoutedWeapons(GraphTree.Parent);
            var changed = weaponIds.Where(weaponId => plan.Graphs.Any(graph => graph != GraphTree.CustomParent && OwnsGraph(weaponId, graph))).ToList();
            var rootNode = FileNode(root, Path.GetFileName(GraphTree.CustomParent));
            rootNode.Children.AddRange(changed.Select(weaponId => new DeliveryTreeNode
            {
                Label = $"{WeaponLab.Core.Blender.WeaponLabels.For(weaponId)}  ({weaponId})",
                Detail = "re-routed to its custom graphs: " + string.Join(", ", plan.Graphs.Where(graph => graph != GraphTree.CustomParent && OwnsGraph(weaponId, graph)).Select(Path.GetFileName)),
                Action = DeliveryAction.Updates,
            }));
            rootNode.Children.Add(new DeliveryTreeNode
            {
                Label = $"All weapons this graph routes ({routed.Count})",
                Detail = "unchanged ones keep CS2's animation",
                IsExpanded = false,
                Children = routed.Select(weaponId => new DeliveryTreeNode { Label = weaponId, Detail = changed.Contains(weaponId) ? "changed in this delivery" : "", Action = changed.Contains(weaponId) ? DeliveryAction.Updates : DeliveryAction.Group }).ToList(),
            });
            shared.Add(rootNode);
        }

        shared.AddRange(plan.Graphs.Where(graph => files.ContainsKey(graph + "_c") && !placed.Contains(graph + "_c")).Select(graph => FileNode(files[graph + "_c"], Path.GetFileName(graph))));
        if (files.TryGetValue(AddonManifest.VDataSourceKey, out var vdata))
        {
            var others = plan.VDataValues.Where(value => !blockOwner.ContainsKey(value.Block))
                .Select(value => new DeliveryTreeNode { Label = $"{value.Block}  {VDataGlossary.Describe(value.Path).Name}", Detail = $"{value.Path} = {value.Value}", Action = FileAction(vdata) }).ToList();
            var vdataNode = FileNode(vdata, "weapons.vdata");
            shared.Add(new DeliveryTreeNode
            {
                Label = "weapons.vdata",
                Detail = $"{plan.VDataEdits} value(s), {plan.VDataResets} reset(s){(plan.CompilesVData ? ", compiled" : ", source only")}",
                Action = vdataNode.Action,
                IsExpanded = false,
                Children = others,
            });
        }

        shared.AddRange(files.Values.Where(file => !placed.Contains(file.ManifestKey)).Select(file => FileNode(file, Path.GetFileName(file.ManifestKey))));
        shared.AddRange(plan.Removals.Select(path => AddonManifest.KeyFor(plan.Target, path)).Where(key => placed.Add(key))
            .Select(key => new DeliveryTreeNode { Label = Path.GetFileName(key), Detail = key, Action = DeliveryAction.Removes, AssetPath = WithoutCompiledSuffix(key) }));
        shared.AddRange(plan.Warnings.Where(warning => !weaponIds.Any(weaponId => warning.Subject == weaponId || BlocksFor(connection.FindWeapon(weaponId)!).Contains(warning.Subject)))
            .Select(warning => new DeliveryTreeNode { Label = warning.Subject, Detail = warning.Message, Action = DeliveryAction.Warning, Target = CheckTargetOf(warning.Message, warning.Subject), AssetPath = SkeletonPathIn(warning.Message) }));
        if (plan.HookedPlayerModels.Count > 0)
        {
            shared.Add(new DeliveryTreeNode
            {
                Label = $"Player models using {Path.GetFileName(GraphTree.CustomParent)} ({plan.HookedPlayerModels.Count})",
                Action = DeliveryAction.Hooked,
                IsExpanded = false,
                Children = plan.HookedPlayerModels.Select(model => new DeliveryTreeNode { Label = Path.GetFileNameWithoutExtension(model.Path), Detail = model.Path, Action = DeliveryAction.Hooked }).ToList(),
            });
        }

        var weaponCount = weaponNodes.Count;
        var children = weaponNodes;
        if (shared.Count > 0)
        {
            children.Add(new DeliveryTreeNode { Label = "Shared", Detail = "routing and files no single weapon owns", Children = shared });
        }

        return [new DeliveryTreeNode { Label = plan.Target.Name, Detail = plan.IsEmpty ? "nothing pending" : $"{weaponCount} weapon(s)", Children = children }];
    }

    // ---- Commit toggles ----

    /// <summary>Weapons with something to commit or leave out: edited clips or a custom skeleton in their sandbox.</summary>
    private IEnumerable<string> CommittableWeapons()
    {
        foreach (var sandbox in _sandboxes.List())
        {
            var hasEdits = sandbox.Manifest.AllClips().Any(clip => ClipExporter.LastExport(sandbox, WeaponLab.Core.Blender.RigPackage.ClipId(clip.ResourcePath)) is { IdenticalToTemplate: false });
            if (hasEdits || WeaponLab.Core.Skeletons.CustomSkeletons.Load(sandbox) is not null)
            {
                yield return sandbox.Manifest.WeaponId;
            }
        }
    }

    /// <summary>
    /// A weapon's clip rows: every delivered clip, plus edited (or re-targeted)
    /// clips that are left out, unticked. Leaving out a clip a custom skeleton
    /// needs is a problem: it stays on the old skeleton and the parts freeze.
    /// </summary>
    private List<DeliveryTreeNode> ClipRows(string weaponId, DeliveryPlan plan, IReadOnlyDictionary<string, DeliveryFile> files, Func<DeliveryFile, string, DeliveryTreeNode> fileNode)
    {
        var rows = new List<DeliveryTreeNode>();
        var settings = _profile.Weapon(weaponId);
        var sandbox = _sandboxes.Exists(weaponId) ? _sandboxes.Open(weaponId) : null;
        var setup = sandbox is null ? null : WeaponLab.Core.Skeletons.CustomSkeletons.Load(sandbox);
        foreach (var clip in plan.Clips.Where(clip => clip.WeaponId == weaponId))
        {
            if (files.TryGetValue(clip.CustomPath + "_c", out var file))
            {
                var made = fileNode(file, $"{clip.ClipId}  ({clip.Changes})");
                var clipId = clip.ClipId;
                rows.Add(new DeliveryTreeNode
                {
                    Label = made.Label,
                    Detail = made.Detail,
                    Action = made.Action,
                    CanToggle = true,
                    Included = true,
                    Toggled = value => CommitAsync(() => SetClipCommitted(weaponId, clipId, value)),
                });
            }
        }

        if (sandbox is null || settings.Animations != AnimationMode.Custom)
        {
            return rows;
        }

        foreach (var clip in sandbox.Manifest.AllClips())
        {
            var clipId = WeaponLab.Core.Blender.RigPackage.ClipId(clip.ResourcePath);
            if (!settings.ExcludedClips.Contains(clipId))
            {
                continue;
            }

            var needed = setup is not null && !WeaponLab.Core.Weapons.WeaponCatalog.IsWorldClip(clip.ResourcePath) && (!setup.FromAddon || setup.Templates.ContainsKey(clip.ResourcePath));
            rows.Add(new DeliveryTreeNode
            {
                Label = clipId,
                Detail = "left out of this push",
                CanToggle = true,
                Included = false,
                Toggled = value => CommitAsync(() => SetClipCommitted(weaponId, clipId, value)),
                Problem = needed
                    ? $"Not committing {clipId} leaves it on {Path.GetFileName(setup!.FromSkeleton)}: in game the weapon's parts stay still during it, because the custom skeleton {Path.GetFileName(setup.Path)} needs every clip of the weapon."
                    : null,
            });
        }

        return rows;
    }

    private void SetClipCommitted(string weaponId, string clipId, bool committed)
    {
        var excluded = _profile.EnsureWeapon(weaponId).ExcludedClips;
        if (committed)
        {
            excluded.Remove(clipId);
        }
        else
        {
            excluded.Add(clipId);
        }
    }

    /// <summary>
    /// Leaving out a weapon removes what Weapon Lab delivered for it. When that
    /// includes a custom skeleton another addon file (a model, a graph) still
    /// uses, that file breaks in game: that is a problem.
    /// </summary>
    private string? WeaponProblem(string weaponId, DeliveryPlan plan)
    {
        var setup = _sandboxes.Exists(weaponId) ? WeaponLab.Core.Skeletons.CustomSkeletons.Load(_sandboxes.Open(weaponId)) : null;
        if (setup is null || setup.FromAddon || plan.Previous?.Files.ContainsKey(setup.Path + "_c") != true)
        {
            return null;
        }

        var users = plan.BrokenReferences.Where(warning => warning.Message.Contains(setup.Path, StringComparison.OrdinalIgnoreCase)).Select(warning => warning.Subject).ToList();
        return users.Count == 0 ? null
            : $"{string.Join(", ", users)} still use{(users.Count == 1 ? "s" : "")} {Path.GetFileName(setup.Path)}. Not committing removes that skeleton, so {(users.Count == 1 ? "it breaks" : "they break")} in game.";
    }

    /// <summary>Applies a commit toggle to the profile, then reviews again so the tree shows the result.</summary>
    private async void CommitAsync(Action change)
    {
        change();
        SaveProfile();
        await ReviewDeliveryAsync();
        await RefreshStatusAsync();
    }

    /// <summary>The weapons a graph reaches through its weapon variations (viewmodel_gun.vnmgraph+ak47 → rifle_ak).</summary>
    private List<string> RoutedWeapons(string graphPath)
    {
        if (_connection is not { } connection || !connection.Graphs.TryGetValue(graphPath, out var root))
        {
            return [];
        }

        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(root.Resources.Where(resource => resource.EndsWith(".vnmgraph", StringComparison.Ordinal)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var graph = queue.Dequeue();
            if (!seen.Add(graph) || !connection.Graphs.TryGetValue(graph, out var info))
            {
                continue;
            }

            foreach (var weapon in DeliveryPlanner.GraphOwners(connection, info))
            {
                reached.Add(weapon);
            }

            foreach (var child in info.Resources.Where(resource => resource.EndsWith(".vnmgraph", StringComparison.Ordinal)))
            {
                queue.Enqueue(child);
            }
        }

        return reached.Order(StringComparer.Ordinal).ToList();
    }

    private bool OwnsGraph(string weaponId, string graph) =>
        _connection is { } connection && connection.Graphs.TryGetValue(graph.Replace("_custom.vnmgraph", ".vnmgraph", StringComparison.Ordinal), out var info)
        && DeliveryPlanner.GraphOwners(connection, info).Contains(weaponId);

    private List<PlanRow> PlanRows(DeliveryPlan plan)
    {
        var rows = new List<PlanRow>();
        if (plan.IsEmpty)
        {
            rows.Add(new PlanRow("status", $"Nothing pending for {plan.Target.Name}.", false));
            return rows;
        }

        // What would keep the work from showing in game comes first.
        rows.AddRange(plan.Warnings.Select(warning => new PlanRow("warning", $"{warning.Subject}: {warning.Message}", true) { Target = CheckTargetOf(warning.Message, warning.Subject) }));
        if (plan.Graphs.Count > 0 && plan.HookedPlayerModels.Count > 0)
        {
            rows.Add(new PlanRow("hookup", $"{plan.HookedPlayerModels.Count} player model(s) use {GraphTree.CustomParent}: {string.Join(", ", plan.HookedPlayerModels.Take(4).Select(model => Path.GetFileNameWithoutExtension(model.Path)))}{(plan.HookedPlayerModels.Count > 4 ? ", …" : "")}", false));
        }

        rows.AddRange(plan.Clips.Select(clip => new PlanRow("clip", $"{clip.WeaponId}/{clip.ClipId} → {clip.CustomPath} ({clip.Changes})", false)));
        rows.AddRange(plan.Graphs.Select(graph => new PlanRow("graph", graph, false)));
        if (plan.CompilesVData)
        {
            rows.Add(new PlanRow("vdata", $"{plan.VDataEdits} value(s) set, {plan.VDataResets} back to {(plan.OriginalVData is null ? "CS2's" : "the addon's own")} values, compiled into scripts/weapons.vdata_c", false));
        }

        var previous = AddonManifest.Read(plan.Target);
        var previouslyOurs = previous?.Files.Keys.Select(key => Path.GetFullPath(AddonManifest.Resolve(plan.Target, key))).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var file in plan.Files.Where(file => file.Restores))
        {
            rows.Add(new PlanRow("restores", $"{file.Destination} (the addon's own version comes back)", false));
        }

        foreach (var path in plan.Replaced.Where(path => !path.EndsWith(AddonManifest.FileName, StringComparison.OrdinalIgnoreCase)))
        {
            if (plan.Files.Any(file => file.Restores && file.Destination == path))
            {
                continue;
            }

            var foreign = !previouslyOurs.Contains(Path.GetFullPath(path));
            var graph = path.EndsWith(".vnmgraph_c", StringComparison.OrdinalIgnoreCase);
            rows.Add(foreign && graph
                ? new PlanRow("updates", $"{path} (the addon's own graph: patched, its routing kept; original saved)", false)
                : new PlanRow("replaces", foreign ? $"{path} (the addon's own file: replaced; original saved and restored when no longer delivered)" : path, foreign));
        }

        if (plan.CompilesVData && File.Exists(plan.Target.VDataCompiledPath) && !previouslyOurs.Contains(Path.GetFullPath(plan.Target.VDataCompiledPath)))
        {
            rows.Add(new PlanRow("replaces", $"{plan.Target.VDataCompiledPath} (the addon's own compiled vdata, recompiled; backed up first)", true));
        }

        rows.AddRange(plan.Removals.Select(path => new PlanRow("removes", path, false)));
        rows.AddRange(plan.Notes.Select(note => new PlanRow("note", note, note.StartsWith("Repair", StringComparison.Ordinal) || note.Contains("not played", StringComparison.Ordinal) || note.Contains("replaced", StringComparison.Ordinal) || note.StartsWith("Replaces the addon", StringComparison.Ordinal) || note.Contains("skipped", StringComparison.Ordinal))));
        return rows;
    }

    /// <summary>
    /// Asks before a push that replaces another profile's clips or leaves addon
    /// files pointing at removed ones; true to go ahead.
    /// </summary>
    private async Task<bool> ConfirmRiskyDeliveryAsync(DeliveryPlan plan)
    {
        var risks = plan.Overwrites.Concat(plan.BrokenReferences).ToList();
        if (risks.Count == 0)
        {
            return true;
        }

        var message = string.Join("\n\n", risks.Take(12).Select(risk => $"{risk.Subject} {risk.Message}")) + (risks.Count > 12 ? $"\n\n… and {risks.Count - 12} more." : "");
        return await Dialogs.ConfirmAsync(this, "Check before applying",
            message + $"\n\nPush to {plan.Target.Name} anyway? Everything touched is backed up and 'Undo last push' restores it.", "Push anyway", destructive: true);
    }

    private async void OnDeliverClicked(object? sender, RoutedEventArgs e)
    {
        if (_connection is not { } connection || Target() is not { } target)
        {
            return;
        }

        _delivering = true;
        RefreshDeliverView();
        try
        {
            // Plan again so the apply uses the latest exports and edits.
            var plan = await Task.Run(() => DeliveryPlanner.Plan(_sandboxes, connection, target, _profile));
            if (!await ConfirmRiskyDeliveryAsync(plan))
            {
                Log($"Apply to {target.Name} cancelled; nothing was written.");
                return;
            }

            var result = await AddonDelivery.DeliverAsync(plan, connection.Install, _sandboxes.WorkspacePath, new Progress<string>(SetStatus));
            Log($"Applied profile '{_profile.Name}' to {target.Name}: {result.FilesWritten} file(s) written, {result.FilesRemoved} removed. Backup: {result.BackupFolder}.");
            // Pushed work leaves the profile's pending list: files count as pushed, graph edits are live now.
            plan.RecordIn(_profile);
            SaveProfile();

            foreach (var warning in plan.Warnings)
            {
                Log($"Warning, {warning.Subject}: {warning.Message}");
            }

            _reviewedPlan = null;
            PlanList.ItemsSource = new[] { new PlanRow("done", $"Pushed to {target.Name}. Not tested in game yet; 'Undo last push' restores the addon.", false) };
            _addonVDataFor = null;
            await RefreshStatusAsync();
        }
        catch (DeliveryException error)
        {
            Log(error.Message);
            ShowPlanError(error.Message);
            await SaveCompilerLogAsync(error.Log);
            await Dialogs.ShowAsync(this, "Apply failed", error.Message + (CheckTargetOf(error.Message) is not null ? "\n\nThe Show button beside the error in the Deliver tab opens what it is about." : ""));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log($"Apply stopped before writing anything: {error.Message}");
            ShowPlanError("Nothing was written. " + error.Message);
            await Dialogs.ShowAsync(this, "Apply stopped", "Nothing was written. " + error.Message + (CheckTargetOf(error.Message) is not null ? "\n\nThe Show button beside the error in the Deliver tab opens what it is about." : ""));
        }
        finally
        {
            _delivering = false;
            RefreshDeliverView();
        }
    }

    private async void OnUndoDeliveryClicked(object? sender, RoutedEventArgs e)
    {
        if (Target() is not { } target || AddonDelivery.LastDelivery(_sandboxes.WorkspacePath, target.Name) is not { } last)
        {
            return;
        }

        if (!await Dialogs.ConfirmAsync(this, "Undo last push",
                $"Put every file the last apply to {target.Name} touched back the way it was before that apply?", "Undo"))
        {
            return;
        }

        try
        {
            AddonDelivery.Rollback(last);
            Log($"Undid the last apply to {target.Name}; its files are back to how they were.");
            _addonVDataFor = null;
            _reviewedPlan = null;
            PlanList.ItemsSource = null;
            await RefreshStatusAsync();
        }
        catch (Exception error) when (error is DeliveryException or IOException or UnauthorizedAccessException)
        {
            Log($"Could not undo: {error.Message}");
        }

        RefreshDeliverView();
    }
}
