using System.Diagnostics;
using Photon.App.Interop;
using Photon.Core.Models;
using Photon.Core.Services;
using Photon.Core.Util;

namespace Photon.App.Services;

/// <summary>
/// "Ghost sort": mirrors the planned destination tree with .lnk shortcuts so the user can
/// browse exactly where every file would land, without copying a single byte.
/// Fully journaled (OperationKind "Preview sort"), so History undo wipes the ghost tree.
/// </summary>
public sealed class PreviewSortService
{
    public const string PreviewFolderName = "_Photon Preview";

    public async Task<SortResult> RunPreviewAsync(SortPlan plan, IProgress<SortProgress>? progress, CancellationToken ct)
    {
        var previewRoot = Path.Combine(plan.DestinationRoot, PreviewFolderName);
        var result = new SortResult { DestinationRoot = previewRoot };
        var sw = Stopwatch.StartNew();

        var journal = new SortJournal
        {
            TimestampUtc = DateTime.UtcNow,
            OperationKind = "Preview sort",
            SourceFolder = Path.GetDirectoryName(plan.Items.FirstOrDefault()?.Source.FilePath ?? "") ?? "",
            DestinationRoot = previewRoot,
            Action = SortAction.Copy,
        };

        await Task.Run(async () =>
        {
            var createdDirs = new List<string>();
            var knownDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void EnsureDirectory(string dir)
            {
                if (!knownDirs.Add(dir)) return;
                // Record every level this call brings into existence so undo can prune them.
                var missing = new Stack<string>();
                var probe = dir;
                while (!string.IsNullOrEmpty(probe) && !Directory.Exists(probe))
                {
                    missing.Push(probe);
                    probe = Path.GetDirectoryName(probe) ?? "";
                }
                Directory.CreateDirectory(dir);
                while (missing.Count > 0)
                {
                    var d = missing.Pop();
                    if (d.Equals(dir, StringComparison.OrdinalIgnoreCase) || knownDirs.Add(d))
                        createdDirs.Add(d);
                }
            }

            int done = 0;
            foreach (var item in plan.Items)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
                try
                {
                    var relative = Path.GetRelativePath(plan.DestinationRoot, item.PlannedDestination);
                    if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                        relative = Path.GetFileName(item.PlannedDestination); // planned path escaped the root; flatten it
                    var linkDir = Path.Combine(previewRoot, Path.GetDirectoryName(relative) ?? "");
                    EnsureDirectory(linkDir);
                    var lnkPath = PathSanitizer.MakeUnique(
                        Path.Combine(linkDir, Path.GetFileName(relative) + ".lnk"), File.Exists);
                    if (OperatingSystem.IsWindows())
                        ShellLink.CreateShortcut(lnkPath, item.Source.FilePath, $"Photon preview of {item.PlannedDestination}");
                    else
                        throw new PlatformNotSupportedException("Shortcut creation requires Windows.");
                    journal.Entries.Add(new JournalEntry
                    {
                        Operation = JournalOperation.LinkCreated,
                        OriginalPath = item.Source.FilePath,
                        NewPath = lnkPath,
                        SizeBytes = item.Source.SizeBytes,
                    });
                    result.Processed++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add((item.Source.FilePath, ex.Message));
                }

                done++;
                if (progress is not null && (done % 25 == 0 || done == plan.Items.Count))
                {
                    var perSec = done / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    progress.Report(new SortProgress
                    {
                        CurrentFile = item.Source.FilePath,
                        ProcessedCount = done,
                        TotalCount = plan.Items.Count,
                        FilesPerSecond = perSec,
                        EstimatedRemaining = perSec > 0
                            ? TimeSpan.FromSeconds((plan.Items.Count - done) / perSec)
                            : null,
                    });
                }
            }

            // Deepest first so undo can prune child folders before their parents.
            journal.CreatedDirectories.AddRange(createdDirs
                .OrderByDescending(d => d.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .ThenByDescending(d => d.Length));

            // Save even when cancelled: a partial ghost tree must still be undoable from History.
            if (journal.Entries.Count > 0 || journal.CreatedDirectories.Count > 0)
                await new JournalService().SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
        }, CancellationToken.None).ConfigureAwait(false);

        result.Elapsed = sw.Elapsed;

        if (!result.Cancelled && result.Processed > 0 && OperatingSystem.IsWindows())
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{previewRoot}\"") { UseShellExecute = true }); }
            catch { /* opening Explorer is a courtesy, never a failure */ }
        }

        return result;
    }
}
