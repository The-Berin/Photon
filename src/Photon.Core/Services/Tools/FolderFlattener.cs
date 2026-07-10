using System.Diagnostics;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>
/// Feature 3: moves every file in subfolders up to the root, then prunes the folders
/// the moves left empty. Journaled so History → Undo can put everything back.
/// </summary>
public sealed class FolderFlattener : IFolderFlattener
{
    private readonly IJournalService _journal;

    public FolderFlattener() : this(new JournalService()) { }

    public FolderFlattener(IJournalService journal) => _journal = journal;

    public Task<FlattenPlan> BuildPlanAsync(FlattenOptions options, CancellationToken ct = default)
        => Task.Run(() => BuildPlan(options, ct), ct);

    private static FlattenPlan BuildPlan(FlattenOptions options, CancellationToken ct)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Root));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Folder not found: {root}");
        var plan = new FlattenPlan { Root = root };

        // Names already present directly in the root (files and folders) are taken.
        var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
                takenNames.Add(Path.GetFileName(entry));
        }
        catch
        {
            plan.Warnings.Add("Could not list the root folder.");
        }
        bool Taken(string path) => takenNames.Contains(Path.GetFileName(path));

        foreach (var file in ToolsCommon.EnumerateFilesSafe(root, recursive: true,
                     dir => plan.Warnings.Add($"Inaccessible: {dir}")))
        {
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(file) ?? "";
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) continue; // already at the root
            if (options.MediaOnly && !ToolsCommon.MediaExtensions.Contains(Path.GetExtension(file))) continue;

            var name = Path.GetFileName(file);
            var dest = Path.Combine(root, name);
            if (Taken(dest))
            {
                switch (options.ConflictPolicy)
                {
                    case FlattenConflictPolicy.AppendNumber:
                        dest = PathSanitizer.MakeUnique(dest, Taken);
                        break;
                    case FlattenConflictPolicy.AppendFolderName:
                        var folder = Path.GetFileName(dir);
                        dest = Path.Combine(root,
                            $"{Path.GetFileNameWithoutExtension(name)} [{folder}]{Path.GetExtension(name)}");
                        if (Taken(dest)) dest = PathSanitizer.MakeUnique(dest, Taken);
                        break;
                    case FlattenConflictPolicy.Skip:
                        plan.Warnings.Add($"Skipped (name already in root): {file}");
                        continue;
                }
            }
            takenNames.Add(Path.GetFileName(dest));
            plan.Items.Add(new FlattenPlanItem { SourcePath = file, DestinationPath = dest });
        }

        plan.FoldersToRemove = CountRemovableFolders(root, plan);
        return plan;
    }

    /// <summary>Folders that will be empty once every planned move has happened.</summary>
    private static int CountRemovableFolders(string root, FlattenPlan plan)
    {
        var moving = plan.Items.Select(i => i.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var emptiedByRun = AncestorDirectories(plan.Items.Select(i => i.SourcePath), root);
        var removable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // A child path is always longer than its parent's, so length-descending visits children first.
        foreach (var dir in ToolsCommon.EnumerateDirectoriesSafe(root).OrderByDescending(d => d.Length))
        {
            if (!emptiedByRun.Contains(dir)) continue; // pre-existing empty folders stay
            bool empty = true;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                    if (!moving.Contains(file)) { empty = false; break; }
                if (empty)
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        if (!removable.Contains(sub)) { empty = false; break; }
            }
            catch
            {
                empty = false;
            }
            if (empty) removable.Add(dir);
        }
        return removable.Count;
    }

    /// <summary>Every directory (below root) on the parent chain of a moved file: only these can have been emptied by the flatten.</summary>
    private static HashSet<string> AncestorDirectories(IEnumerable<string> movedFiles, string root)
    {
        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in movedFiles)
        {
            var dir = Path.GetDirectoryName(file);
            while (!string.IsNullOrEmpty(dir)
                   && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)
                   && ancestors.Add(dir))
                dir = Path.GetDirectoryName(dir);
        }
        return ancestors;
    }

    public async Task<FlattenResult> ExecuteAsync(FlattenPlan plan, FlattenOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new FlattenResult();
        var journal = new SortJournal
        {
            TimestampUtc = DateTime.UtcNow,
            OperationKind = "Flatten",
            SourceFolder = plan.Root,
            DestinationRoot = plan.Root,
            Action = SortAction.Move,
        };

        var checkpoint = new JournalCheckpoint(_journal, journal);
        await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            int done = 0;
            long bytes = 0;
            bool cancelled = false;
            foreach (var item in plan.Items)
            {
                if (ct.IsCancellationRequested) { cancelled = true; break; }
                try
                {
                    var dest = item.DestinationPath;
                    if (File.Exists(dest) || Directory.Exists(dest))
                    {
                        // The plan was collision-free; something appeared since planning.
                        if (options.ConflictPolicy == FlattenConflictPolicy.Skip)
                        {
                            result.Skipped++;
                            continue;
                        }
                        dest = PathSanitizer.MakeUnique(dest, p => File.Exists(p) || Directory.Exists(p));
                    }
                    long size = 0;
                    try { size = new FileInfo(item.SourcePath).Length; } catch { /* size is best-effort */ }
                    File.Move(item.SourcePath, dest);
                    journal.Entries.Add(new JournalEntry
                    {
                        Operation = JournalOperation.Moved,
                        OriginalPath = item.SourcePath,
                        NewPath = dest,
                        SizeBytes = size,
                    });
                    result.Moved++;
                    bytes += size;
                }
                catch (Exception ex)
                {
                    result.Errors.Add((item.SourcePath, ex.Message));
                }
                done++;
                progress?.Report(ToolsCommon.MakeProgress(item.SourcePath, done, plan.Items.Count, bytes, 0, stopwatch));
                // A crash mid-flatten must not lose the record of completed moves.
                checkpoint.MaybeSave();
            }

            // Deepest-first so parents empty out as their children disappear. Only folders
            // this run actually emptied (ancestors of a moved file) are deleted: undo cannot
            // recreate a pre-existing empty folder, so it is not ours to remove.
            if (options.RemoveEmptyFolders && !cancelled)
            {
                var emptiedByRun = AncestorDirectories(journal.Entries.Select(e => e.OriginalPath), plan.Root);
                foreach (var dir in ToolsCommon.EnumerateDirectoriesSafe(plan.Root).OrderByDescending(d => d.Length))
                {
                    if (ct.IsCancellationRequested) break;
                    if (!emptiedByRun.Contains(dir)) continue;
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir);
                            result.FoldersRemoved++;
                        }
                    }
                    catch { /* locked or refilled — leave it */ }
                }
            }
        }, CancellationToken.None);

        if (journal.Entries.Count > 0)
        {
            await _journal.SaveAsync(journal, CancellationToken.None);
            result.JournalPath = ToolsCommon.JournalFilePath(_journal, journal);
        }
        return result;
    }
}
