using System.Text.Json;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;

namespace WeaponLab.Core.Delivery;

public sealed record DeliveryResult(string BackupFolder, int FilesWritten, int FilesRemoved, string? CompilerLog);

public sealed class DeliveryException(string message, string? log = null) : Exception(message)
{
    public string? Log { get; } = log;
}

/// <summary>
/// Runs a <see cref="DeliveryPlan"/>: backs up every file it will touch,
/// writes and removes files, compiles vdata with resourcecompiler and checks
/// the result. Any failure restores the backups. Past deliveries can be
/// undone from their journal.
/// </summary>
public static class AddonDelivery
{
    public const string ManifestFileName = AddonManifest.FileName;
    private const string JournalFileName = "journal.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string BackupsFolder(string workspacePath) => Path.Combine(workspacePath, "deliveries");

    public static async Task<DeliveryResult> DeliverAsync(DeliveryPlan plan, GameInstall install, string workspacePath, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        var backup = Path.Combine(BackupsFolder(workspacePath), DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        var touched = plan.Files.Select(file => file.Destination).Concat(plan.Removals).ToList();
        if (plan.CompilesVData)
        {
            touched.Add(plan.Target.VDataCompiledPath);
        }

        // The addon's own work is kept before Weapon Lab first changes it: a one-time snapshot, and per-file originals.
        if (AddonOriginals.SnapshotOnce(workspacePath, plan.Target) is { } snapshot)
        {
            progress?.Report($"First delivery to {plan.Target.Name}: its animation and scripts folders were copied to {snapshot}");
        }

        var originals = AddonOriginals.Load(workspacePath, plan.Target.Name);
        foreach (var key in plan.TouchedKeys)
        {
            originals.Record(key, plan.Target, plan.Previous);
        }

        originals.Save();

        var journal = BackUp(touched, backup, plan.Target.Name, plan.Manifest.Profile);
        progress?.Report($"Backed up {journal.Entries.Count(entry => entry.Backup is not null)} existing file(s) to {backup}");

        string? log = null;
        try
        {
            foreach (var file in plan.Files)
            {
                AtomicFile.WriteAllBytes(file.Destination, file.Bytes);
            }

            foreach (var path in plan.Removals)
            {
                File.Delete(path);
            }

            progress?.Report($"Wrote {plan.Files.Count} file(s) into {plan.Target.Name}.");
            if (plan.CompilesVData)
            {
                progress?.Report("Compiling weapons.vdata with resourcecompiler…");
                log = await VDataCompiler.CompileAsync(install, plan.Target.VDataSourcePath, cancellation);
                if (!File.Exists(plan.Target.VDataCompiledPath))
                {
                    throw new DeliveryException("resourcecompiler finished but wrote no weapons.vdata_c.", log);
                }

                VDataCompiler.VerifyCompiled(File.ReadAllBytes(plan.Target.VDataCompiledPath), plan.VDataValues, plan.Swaps, plan.StockVData, plan.OriginalVData);
                plan.Manifest.Files[AddonManifest.CompiledVDataPath] = GameArchive.Sha256(File.ReadAllBytes(plan.Target.VDataCompiledPath));
                plan.Manifest.Write(plan.Target);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Restore(journal);
            WriteJournal(backup, journal with { RolledBack = true, Error = error.Message });
            var compilerLog = (error as VDataCompileException)?.Log ?? (error as DeliveryException)?.Log ?? log;
            throw new DeliveryException($"Delivery failed and was rolled back: {error.Message}", compilerLog);
        }

        // Set to CS2 default: what this delivery left is the addon's own from now on, so nothing brings the old files back.
        if (plan.ForgetOriginals.Count > 0)
        {
            foreach (var key in plan.ForgetOriginals)
            {
                originals.Forget(key);
            }

            originals.Save();
        }

        return new DeliveryResult(backup, plan.Files.Count, plan.Removals.Count, log);
    }

    /// <summary>A past delivery, newest first in <see cref="History"/>.</summary>
    public sealed record DeliveryRecord(string Folder, string Addon, string? Profile, DateTimeOffset DeliveredUtc, int Files, bool RolledBack, string? Error);

    public static IReadOnlyList<DeliveryRecord> History(string workspacePath)
    {
        var folder = BackupsFolder(workspacePath);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateDirectories(folder)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Select(path => (Path: path, Journal: ReadJournal(path)))
            .Where(item => item.Journal is not null)
            .Select(item => new DeliveryRecord(item.Path, item.Journal!.Addon, item.Journal.Profile, item.Journal.DeliveredUtc, item.Journal.Entries.Count, item.Journal.RolledBack, item.Journal.Error))
            .ToList();
    }

    /// <summary>The newest delivery to this addon that has not been undone, or null.</summary>
    public static string? LastDelivery(string workspacePath, string addonName)
    {
        return History(workspacePath).FirstOrDefault(record => !record.RolledBack && record.Addon == addonName)?.Folder;
    }

    /// <summary>
    /// Undoes one delivery: every file it touched goes back to how it was
    /// before it. Only the newest active delivery to an addon can be undone
    /// safely; older ones would overwrite later deliveries.
    /// </summary>
    public static void Rollback(string backupFolder)
    {
        var journal = ReadJournal(backupFolder) ?? throw new DeliveryException("No delivery journal in " + backupFolder);
        if (journal.RolledBack)
        {
            throw new DeliveryException("This delivery was already undone.");
        }

        var newer = History(Path.GetDirectoryName(Path.GetDirectoryName(backupFolder))!)
            .FirstOrDefault(record => !record.RolledBack && record.Addon == journal.Addon);
        if (newer is not null && !string.Equals(newer.Folder, backupFolder, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeliveryException("A newer delivery to this addon is still active; undo that one first, or set the weapon to Default and apply the profile.");
        }

        Restore(journal);
        WriteJournal(backupFolder, journal with { RolledBack = true });
    }

    private static Journal BackUp(List<string> paths, string backup, string addon, string? profile)
    {
        Directory.CreateDirectory(Path.Combine(backup, "files"));
        var entries = new List<JournalEntry>();
        for (var index = 0; index < paths.Count; index++)
        {
            string? saved = null;
            if (File.Exists(paths[index]))
            {
                saved = $"files/{index}.previous";
                File.Copy(paths[index], Path.Combine(backup, saved));
            }

            entries.Add(new JournalEntry(paths[index], saved));
        }

        var journal = new Journal(addon, profile, DateTimeOffset.UtcNow, entries, RolledBack: false, Error: null) { Folder = backup };
        WriteJournal(backup, journal);
        return journal;
    }

    private static void Restore(Journal journal)
    {
        foreach (var entry in journal.Entries.AsEnumerable().Reverse())
        {
            if (entry.Backup is null)
            {
                if (File.Exists(entry.Destination))
                {
                    File.Delete(entry.Destination);
                }
            }
            else
            {
                AtomicFile.WriteAllBytes(entry.Destination, File.ReadAllBytes(Path.Combine(journal.Folder, entry.Backup)));
            }
        }
    }

    private static void WriteJournal(string folder, Journal journal) =>
        AtomicFile.WriteAllText(Path.Combine(folder, JournalFileName), JsonSerializer.Serialize(journal, Json) + "\n");

    private static Journal? ReadJournal(string folder)
    {
        var path = Path.Combine(folder, JournalFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path), Json);
        return journal is null ? null : journal with { Folder = folder };
    }

    /// <param name="Backup">Saved copy relative to the backup folder, or null if the file was new.</param>
    private sealed record JournalEntry(string Destination, string? Backup);

    private sealed record Journal(string Addon, string? Profile, DateTimeOffset DeliveredUtc, IReadOnlyList<JournalEntry> Entries, bool RolledBack, string? Error)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public string Folder { get; init; } = "";
    }
}
