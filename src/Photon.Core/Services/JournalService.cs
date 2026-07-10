using System.Text.Json;
using System.Text.Json.Serialization;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>Default <see cref="IJournalService"/>: JSON journals in %APPDATA%\Photon\Journals plus undo.</summary>
public sealed class JournalService : IJournalService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string JournalDirectory { get; }

    public JournalService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Photon", "Journals"))
    { }

    public JournalService(string journalDirectory)
    {
        JournalDirectory = journalDirectory;
        Directory.CreateDirectory(journalDirectory);
    }

    /// <summary>File-name convention shared with SortExecutor so it can report the saved journal path.</summary>
    internal static string FileNameFor(SortJournal journal) =>
        $"{journal.TimestampUtc:yyyyMMdd-HHmmss}-{journal.Id}.json";

    public async Task SaveAsync(SortJournal journal, CancellationToken ct = default)
    {
        var path = Path.Combine(JournalDirectory, FileNameFor(journal));
        // Write-then-rename: a crash mid-save must never leave a truncated journal
        // in place of a previously good one (LoadAll silently skips corrupt files).
        var temp = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, journal, JsonOptions, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }

    public List<SortJournal> LoadAll()
    {
        var journals = new List<SortJournal>();
        if (!Directory.Exists(JournalDirectory)) return journals;
        foreach (var file in Directory.EnumerateFiles(JournalDirectory, "*.json"))
        {
            try
            {
                var journal = JsonSerializer.Deserialize<SortJournal>(File.ReadAllText(file), JsonOptions);
                if (journal is not null) journals.Add(journal);
            }
            catch
            {
                // Corrupt journal file: skip it rather than break the History window.
            }
        }
        return [.. journals.OrderByDescending(j => j.TimestampUtc)];
    }

    public SortJournal? LoadLatestUndoable() =>
        LoadAll().FirstOrDefault(j => j.UndoneAtUtc is null);

    public Task<UndoResult> UndoAsync(SortJournal journal,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default)
        // Cancellation must yield an UndoResult (Cancelled = true), never a faulted/cancelled task.
        => Task.Run(() => Undo(journal, progress, ct), CancellationToken.None);

    private UndoResult Undo(SortJournal journal, IProgress<SortProgress>? progress, CancellationToken ct)
    {
        var result = new UndoResult();
        var total = journal.Entries.Count;

        // Reverse order: the last operation performed is the first one undone.
        for (var i = journal.Entries.Count - 1; i >= 0; i--)
        {
            if (ct.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }

            var entry = journal.Entries[i];
            try
            {
                // Entries a previous (cancelled or partially failed) pass already reversed
                // are never replayed — replaying Overwrote would delete the restored file.
                if (!entry.Undone && UndoEntry(entry, journal, result))
                    entry.Undone = true;
            }
            catch (Exception ex)
            {
                result.Errors.Add((entry.NewPath ?? entry.OriginalPath, ex.Message));
            }

            progress?.Report(new SortProgress
            {
                CurrentFile = entry.NewPath ?? entry.OriginalPath,
                ProcessedCount = total - i,
                TotalCount = total,
            });
        }

        if (!result.Cancelled)
        {
            PruneCreatedDirectories(journal, result);
            if (!string.IsNullOrEmpty(journal.BackupFolder))
                TryRemoveEmptyDirectory(journal.BackupFolder, result, countIt: false);

            // Only a pass that conclusively reversed every entry forfeits the journal's
            // undo; failed entries leave it retryable from History.
            if (journal.Entries.All(e => e.Undone))
                journal.UndoneAtUtc = DateTime.UtcNow;
        }

        // Always re-save (even after cancel/failure): the per-entry undo state must
        // survive so a retry skips the work the first pass already did.
        try
        {
            SaveAsync(journal, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            result.Errors.Add((FileNameFor(journal), "Could not re-save journal: " + ex.Message));
        }

        return result;
    }

    /// <summary>Reverses one entry. Returns true when the entry is conclusively undone (nothing left to retry).</summary>
    private static bool UndoEntry(JournalEntry entry, SortJournal journal, UndoResult result)
    {
        var action = journal.Action;
        switch (entry.Operation)
        {
            case JournalOperation.Copied:
            case JournalOperation.LinkCreated:
                if (entry.NewPath is not null && File.Exists(entry.NewPath))
                {
                    RemoveCopy(entry, journal, result);
                    result.Reversed++;
                }
                return true;

            case JournalOperation.Moved:
            case JournalOperation.RenamedInPlace:
                return MoveBack(entry, result);

            case JournalOperation.MovedToDuplicates:
                // A Copy run left the original in place, so the diverted duplicate is itself a copy: delete it.
                if (action == SortAction.Copy)
                {
                    if (entry.NewPath is not null && File.Exists(entry.NewPath))
                    {
                        RemoveCopy(entry, journal, result);
                        result.Reversed++;
                    }
                    return true;
                }
                return MoveBack(entry, result);

            case JournalOperation.Overwrote:
                // Backup already consumed: an earlier pass restored it, so replaying would
                // delete the just-restored destination file. Treat as already undone.
                if (entry.DisplacedBackupPath is null || !File.Exists(entry.DisplacedBackupPath))
                    return true;

                // First take the overwriting file back out of the destination...
                if (action == SortAction.Copy)
                {
                    if (entry.NewPath is not null && File.Exists(entry.NewPath))
                        RemoveCopy(entry, journal, result);
                }
                else
                {
                    MoveBack(entry, result, count: false);
                }
                // ...then restore the displaced file to the destination.
                if (entry.NewPath is not null)
                {
                    var dest = entry.NewPath;
                    var parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    if (File.Exists(dest))
                    {
                        dest = PathSanitizer.MakeUnique(dest, File.Exists);
                        result.Errors.Add((entry.NewPath, $"Destination occupied while restoring backup; restored to \"{dest}\" instead."));
                    }
                    File.Move(entry.DisplacedBackupPath, dest);
                    result.RestoredFromBackup++;
                }
                result.Reversed++;
                return true;

            case JournalOperation.SkippedExisting:
            default:
                return true; // nothing was done, nothing to undo
        }
    }

    /// <summary>
    /// Removes the copy an undo is reversing — unless the file changed since the sort
    /// (edited, rotated, replaced), in which case it is stashed in the journal's backup
    /// folder instead: undo must never hard-delete the only copy of user-modified content.
    /// </summary>
    private static void RemoveCopy(JournalEntry entry, SortJournal journal, UndoResult result)
    {
        var path = entry.NewPath!;
        if (WasModifiedSinceSort(entry, path) && !string.IsNullOrEmpty(journal.BackupFolder))
        {
            Directory.CreateDirectory(journal.BackupFolder);
            var stash = PathSanitizer.MakeUnique(
                Path.Combine(journal.BackupFolder, Path.GetFileName(path)), File.Exists);
            File.Move(path, stash);
            result.Errors.Add((path, $"File was modified after the sort; preserved at \"{stash}\" instead of deleting."));
            return;
        }
        File.Delete(path);
    }

    private static bool WasModifiedSinceSort(JournalEntry entry, string path)
    {
        if (entry.NewFileWriteTimeUtc is not { } recorded) return false; // no basis recorded
        try
        {
            var info = new FileInfo(path);
            if (info.Length != entry.SizeBytes) return true;
            // 2 s tolerance: FAT/exFAT timestamps are coarse.
            return Math.Abs((info.LastWriteTimeUtc - recorded).TotalSeconds) > 2;
        }
        catch
        {
            return true; // cannot verify: err on the side of preserving
        }
    }

    private static bool MoveBack(JournalEntry entry, UndoResult result, bool count = true)
    {
        if (entry.NewPath is null || !File.Exists(entry.NewPath))
        {
            result.Errors.Add((entry.NewPath ?? entry.OriginalPath, "File to restore no longer exists."));
            return false;
        }

        var target = entry.OriginalPath;
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        // A case-only rename: on a case-insensitive filesystem the "occupied" check below
        // would match the renamed file itself and mangle the name with a _1 suffix, so
        // restore through the same temp-name hop RenameEngine used to apply it.
        if (!string.Equals(entry.NewPath, target, StringComparison.Ordinal)
            && string.Equals(entry.NewPath, target, StringComparison.OrdinalIgnoreCase))
        {
            var temp = entry.NewPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.Move(entry.NewPath, temp);
            try { File.Move(temp, target); }
            catch { File.Move(temp, entry.NewPath); throw; }
            if (count) result.Reversed++;
            return true;
        }

        if (File.Exists(target))
        {
            target = PathSanitizer.MakeUnique(target, File.Exists);
            result.Errors.Add((entry.OriginalPath, $"Original path was occupied; restored to \"{target}\" instead."));
        }
        File.Move(entry.NewPath, target);
        if (count) result.Reversed++;
        return true;
    }

    private static void PruneCreatedDirectories(SortJournal journal, UndoResult result)
    {
        foreach (var dir in journal.CreatedDirectories
                     .OrderByDescending(Depth)
                     .ThenByDescending(d => d, StringComparer.Ordinal))
        {
            TryRemoveEmptyDirectory(dir, result, countIt: true);
        }
    }

    private static int Depth(string path) =>
        path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

    private static void TryRemoveEmptyDirectory(string dir, UndoResult result, bool countIt)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                if (countIt) result.DirectoriesRemoved++;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add((dir, ex.Message));
        }
    }
}
