using System.Diagnostics;
using System.Globalization;
using System.Text;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>
/// Default <see cref="ISortExecutor"/>: journaled copy/move with collision handling,
/// duplicate diversion, atomic-ish partial-file writes, and prompt cancellation.
/// </summary>
public sealed class SortExecutor : ISortExecutor
{
    // 1 MiB chunks: small enough for prompt cancellation, big enough for throughput.
    private const int BufferSize = 1 << 20;
    private const string PartialSuffix = ".photon-partial";
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);

    private readonly IJournalService _journals;

    public SortExecutor() : this(new JournalService()) { }

    public SortExecutor(IJournalService journals) => _journals = journals;

    public Task<SortResult> ExecuteAsync(SortPlan plan, SortOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default)
        // Cancellation must yield a SortResult (Cancelled = true), never a faulted/cancelled task.
        => Task.Run(() => Execute(plan, options, progress, ct), CancellationToken.None);

    private SortResult Execute(SortPlan plan, SortOptions options, IProgress<SortProgress>? progress, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTime.Now;
        var result = new SortResult { DestinationRoot = plan.DestinationRoot };

        var journalId = Guid.NewGuid();
        var journal = new SortJournal
        {
            Id = journalId,
            TimestampUtc = DateTime.UtcNow,
            OperationKind = "Sort",
            SourceFolder = options.SourceFolder,
            DestinationRoot = plan.DestinationRoot,
            Action = options.Action,
            BackupFolder = Path.Combine(_journals.JournalDirectory, $"{journalId}-backup"),
        };

        var knownCreatedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkpoint = new JournalCheckpoint(_journals, journal);
        StreamWriter? log = null;
        List<string>? csv = null;
        long doneBytes = 0;
        var rates = new RateTracker();
        TimeSpan? lastReport = null;

        void Report(string currentFile, long currentFileBytes, bool force)
        {
            var now = stopwatch.Elapsed;
            if (!force && lastReport is not null && now - lastReport.Value < ProgressInterval) return;
            lastReport = now;
            var bytes = doneBytes + currentFileBytes;
            rates.Sample(now, result.Processed, bytes);
            var (filesPerSecond, bytesPerSecond) = rates.Rates();
            var remaining = plan.TotalBytes - bytes;
            var etaSeconds = bytesPerSecond > 0 && remaining > 0 ? remaining / bytesPerSecond : 0;
            progress?.Report(new SortProgress
            {
                CurrentFile = currentFile,
                ProcessedCount = result.Processed,
                TotalCount = plan.Items.Count,
                ProcessedBytes = bytes,
                TotalBytes = plan.TotalBytes,
                FilesPerSecond = filesPerSecond,
                BytesPerSecond = bytesPerSecond,
                // Cap at 30 days so a momentary near-zero rate can't overflow TimeSpan.
                EstimatedRemaining = etaSeconds is > 0 and < 30d * 24 * 3600
                    ? TimeSpan.FromSeconds(etaSeconds)
                    : null,
            });
        }

        try
        {
            EnsureDirectory(plan.DestinationRoot, journal, knownCreatedDirs);

            // Persist the journal before the first operation (its backup folder must be
            // discoverable even if the process dies mid-run) and checkpoint it below so a
            // crash or power loss never leaves completed moves unrecorded and un-undoable.
            checkpoint.SaveNow();

            if (options.WriteLogFile)
            {
                var logPath = Path.Combine(plan.DestinationRoot, $"photon-log-{startedAt:yyyyMMdd-HHmmss}.txt");
                try
                {
                    log = new StreamWriter(logPath, append: false, Encoding.UTF8);
                    result.LogFilePath = logPath;
                    WriteLog(log, $"Photon sort started: {options.Action} {plan.Items.Count} file(s) => {plan.DestinationRoot}");
                }
                catch (Exception ex)
                {
                    result.Errors.Add((logPath, "Could not create log file: " + ex.Message));
                }
            }

            if (options.ExportCsvSummary)
                csv = ["original_path,new_path,date,camera,gps"];

            foreach (var item in plan.Items)
            {
                if (ct.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    break;
                }

                var src = item.Source.FilePath;
                try
                {
                    ProcessItem(item, plan, options, journal, knownCreatedDirs, result, log, csv,
                        bytes => Report(src, bytes, force: false), ct);
                    result.Processed++;
                }
                catch (OperationCanceledException)
                {
                    result.Cancelled = true;
                    WriteLog(log, $"CANCELLED while processing {src}");
                    break;
                }
                catch (Exception ex)
                {
                    // A single bad file never aborts the run.
                    result.Processed++;
                    result.Errors.Add((src, ex.Message));
                    WriteLog(log, $"ERROR {src} : {ex.Message}");
                }

                doneBytes += item.Source.SizeBytes;
                Report(src, 0, force: false);
                checkpoint.MaybeSave();
            }
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
        }
        catch (Exception ex)
        {
            result.Errors.Add((plan.DestinationRoot, ex.Message));
            WriteLog(log, $"FATAL {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            result.Elapsed = stopwatch.Elapsed;

            if (csv is not null)
            {
                var csvPath = Path.Combine(plan.DestinationRoot, $"photon-summary-{startedAt:yyyyMMdd-HHmmss}.csv");
                try
                {
                    File.WriteAllLines(csvPath, csv);
                    result.CsvPath = csvPath;
                }
                catch (Exception ex)
                {
                    result.Errors.Add((csvPath, "Could not write CSV summary: " + ex.Message));
                }
            }

            WriteLog(log, $"Done: {result.Copied} copied, {result.Moved} moved, {result.DuplicatesDiverted} duplicate(s) diverted, "
                + $"{result.Overwrote} overwrote, {result.SkippedExisting} skipped, {result.Errors.Count} error(s)"
                + (result.Cancelled ? " — CANCELLED" : ""));
            try { log?.Dispose(); } catch { /* best effort */ }

            journal.CreatedDirectories.Sort(DeepestFirst);
            try
            {
                _journals.SaveAsync(journal, CancellationToken.None).GetAwaiter().GetResult();
                result.JournalPath = Path.Combine(_journals.JournalDirectory, JournalService.FileNameFor(journal));
            }
            catch (Exception ex)
            {
                result.Errors.Add(("journal", "Could not save journal: " + ex.Message));
            }

            Report("", 0, force: true);
        }

        return result;
    }

    private static void ProcessItem(SortPlanItem item, SortPlan plan, SortOptions options, SortJournal journal,
        HashSet<string> knownCreatedDirs, SortResult result, StreamWriter? log, List<string>? csv,
        Action<long> chunkProgress, CancellationToken ct)
    {
        var src = item.Source.FilePath;
        var size = item.Source.SizeBytes;

        // (a) exact-content duplicates divert to <root>\Duplicates (collision-renamed there);
        // with the subfolder option off they fall through to normal processing below.
        if (item.IsContentDuplicate && options.DetectExactDuplicates && options.MoveDuplicatesToSubfolder)
        {
            var dupDir = Path.Combine(plan.DestinationRoot, "Duplicates");
            EnsureDirectory(dupDir, journal, knownCreatedDirs);
            var dupDest = PathSanitizer.MakeUnique(Path.Combine(dupDir, item.Source.FileName), File.Exists);
            Transfer(item.Source, dupDest, options.Action, chunkProgress, ct);
            journal.Entries.Add(new JournalEntry
            {
                Operation = JournalOperation.MovedToDuplicates,
                OriginalPath = src,
                NewPath = dupDest,
                SizeBytes = size,
                NewFileWriteTimeUtc = SafeWriteTimeUtc(dupDest),
            });
            result.DuplicatesDiverted++;
            WriteLog(log, $"DUPLICATE {src} => {dupDest}");
            AddCsvRow(csv, item, dupDest);
            return;
        }

        // (b) collision handling at the planned destination.
        var dest = item.PlannedDestination;
        string? displacedBackup = null;
        var overwrote = false;
        if (File.Exists(dest))
        {
            switch (options.DuplicateHandling)
            {
                case DuplicateHandling.Rename:
                    dest = PathSanitizer.MakeUnique(dest, File.Exists);
                    result.RenamedOnCollision++;
                    break;

                case DuplicateHandling.Skip:
                    journal.Entries.Add(new JournalEntry
                    {
                        Operation = JournalOperation.SkippedExisting,
                        OriginalPath = src,
                        NewPath = null,
                        SizeBytes = size,
                    });
                    result.SkippedExisting++;
                    WriteLog(log, $"SKIP {src} (destination exists: {dest})");
                    AddCsvRow(csv, item, null);
                    return;

                case DuplicateHandling.Overwrite:
                    // Stash the displaced destination file in the journal backup folder first, so undo can restore it.
                    var backupDir = journal.BackupFolder!;
                    Directory.CreateDirectory(backupDir);
                    displacedBackup = PathSanitizer.MakeUnique(Path.Combine(backupDir, Path.GetFileName(dest)), File.Exists);
                    File.Move(dest, displacedBackup);
                    overwrote = true;
                    break;
            }
        }

        // (c) the transfer itself. A failed or cancelled transfer must put a displaced
        // destination file back where it was: the displacement is not journaled yet, so
        // leaving it stashed would make it vanish with no undo record pointing at it.
        EnsureDirectory(Path.GetDirectoryName(dest)!, journal, knownCreatedDirs);
        try
        {
            Transfer(item.Source, dest, options.Action, chunkProgress, ct);
        }
        catch when (displacedBackup is not null)
        {
            try { if (!File.Exists(dest)) File.Move(displacedBackup, dest); }
            catch { /* rollback is best effort; the stashed file stays on disk */ }
            throw;
        }

        // (d) journal with exact paths.
        if (overwrote)
        {
            journal.Entries.Add(new JournalEntry
            {
                Operation = JournalOperation.Overwrote,
                OriginalPath = src,
                NewPath = dest,
                DisplacedBackupPath = displacedBackup,
                SizeBytes = size,
                NewFileWriteTimeUtc = SafeWriteTimeUtc(dest),
            });
            result.Overwrote++;
            WriteLog(log, $"OVERWRITE {src} => {dest}");
        }
        else if (options.Action == SortAction.Copy)
        {
            journal.Entries.Add(new JournalEntry
            {
                Operation = JournalOperation.Copied,
                OriginalPath = src,
                NewPath = dest,
                SizeBytes = size,
                NewFileWriteTimeUtc = SafeWriteTimeUtc(dest),
            });
            result.Copied++;
            WriteLog(log, $"COPY {src} => {dest}");
        }
        else
        {
            journal.Entries.Add(new JournalEntry
            {
                Operation = JournalOperation.Moved,
                OriginalPath = src,
                NewPath = dest,
                SizeBytes = size,
            });
            result.Moved++;
            WriteLog(log, $"MOVE {src} => {dest}");
        }
        AddCsvRow(csv, item, dest);
    }

    private static void Transfer(MediaFile source, string dest, SortAction action,
        Action<long> chunkProgress, CancellationToken ct)
    {
        var src = source.FilePath;
        if (action == SortAction.Move && SameVolume(src, dest))
        {
            File.Move(src, dest);
            chunkProgress(source.SizeBytes);
            return;
        }

        // Moves flush the copy to stable storage before the source delete: losing the OS
        // write-back cache after a mere close could destroy the only copy of the file.
        CopyViaPartial(src, dest, chunkProgress, ct, flushToDisk: action == SortAction.Move);
        if (action == SortAction.Move)
        {
            try
            {
                // Match Explorer's move semantics: a read-only source must still move.
                var attributes = File.GetAttributes(src);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(src, attributes & ~FileAttributes.ReadOnly);
                File.Delete(src); // source removed ONLY after the copy fully succeeded
            }
            catch
            {
                // Undeletable source: remove the (not yet journaled) destination copy so the
                // failed item leaves no orphan behind and a re-run doesn't rename-collide.
                try { File.Delete(dest); } catch { /* best effort */ }
                throw;
            }
        }
    }

    private static void CopyViaPartial(string src, string dest, Action<long> chunkProgress,
        CancellationToken ct, bool flushToDisk = false)
    {
        var partial = dest + PartialSuffix;
        try
        {
            var info = new FileInfo(src);
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                var buffer = new byte[BufferSize];
                long copied = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    copied += read;
                    chunkProgress(copied);
                }
                if (flushToDisk) output.Flush(flushToDisk: true);
            }

            // Preserve source timestamps (best effort — not every filesystem allows it).
            try
            {
                File.SetCreationTime(partial, info.CreationTime);
                File.SetLastWriteTime(partial, info.LastWriteTime);
            }
            catch { /* non-fatal */ }

            // Atomic-ish: a crash never leaves a half-written file at the final name.
            File.Move(partial, dest);
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { /* best effort */ }
            throw;
        }
    }

    private static DateTime? SafeWriteTimeUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return null; }
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(a)),
            Path.GetPathRoot(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    private static void EnsureDirectory(string dir, SortJournal journal, HashSet<string> known)
    {
        if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) return;
        var missing = new List<string>();
        var current = dir;
        while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
        {
            missing.Add(current);
            current = Path.GetDirectoryName(current);
        }
        Directory.CreateDirectory(dir);
        foreach (var created in missing)
            if (known.Add(created))
                journal.CreatedDirectories.Add(created);
    }

    private static int DeepestFirst(string a, string b)
    {
        var byDepth = Depth(b).CompareTo(Depth(a));
        return byDepth != 0 ? byDepth : string.CompareOrdinal(b, a);

        static int Depth(string path) =>
            path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
    }

    private static void WriteLog(StreamWriter? log, string message)
    {
        if (log is null) return;
        try { log.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}"); } catch { /* best effort */ }
    }

    private static void AddCsvRow(List<string>? csv, SortPlanItem item, string? finalDest)
    {
        if (csv is null) return;
        var file = item.Source;
        var camera = string.Join(' ',
            new[] { file.CameraMake, file.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var gps = file.GpsLatitude is not null && file.GpsLongitude is not null
            ? file.GpsLatitude.Value.ToString("F6", CultureInfo.InvariantCulture) + ","
              + file.GpsLongitude.Value.ToString("F6", CultureInfo.InvariantCulture)
            : "";
        var date = item.ResolvedDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
        csv.Add(string.Join(',',
            CsvField(file.FilePath), CsvField(finalDest ?? ""), CsvField(date), CsvField(camera), CsvField(gps)));
    }

    private static string CsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    /// <summary>Rolling ~5 s window over cumulative (files, bytes) samples for smooth rate/ETA numbers.</summary>
    private sealed class RateTracker
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
        private readonly Queue<(TimeSpan At, int Files, long Bytes)> _samples = new();

        public void Sample(TimeSpan at, int files, long bytes)
        {
            _samples.Enqueue((at, files, bytes));
            while (_samples.Count > 2 && at - _samples.Peek().At > Window)
                _samples.Dequeue();
        }

        public (double FilesPerSecond, double BytesPerSecond) Rates()
        {
            if (_samples.Count < 2) return (0, 0);
            var first = _samples.Peek();
            var last = _samples.Last();
            var seconds = (last.At - first.At).TotalSeconds;
            if (seconds <= 0) return (0, 0);
            return ((last.Files - first.Files) / seconds, (last.Bytes - first.Bytes) / seconds);
        }
    }
}
