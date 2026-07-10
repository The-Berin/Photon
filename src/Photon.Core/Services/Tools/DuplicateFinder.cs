using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>
/// Feature 4: standalone duplicate finder. Files are grouped by size first and only members
/// of same-size groups are ever hashed — that is the entire performance trick.
/// </summary>
public sealed class DuplicateFinder : IDuplicateFinder
{
    private const int ChunkSize = 64 * 1024;

    private readonly IJournalService _journal;

    public DuplicateFinder() : this(new JournalService()) { }

    public DuplicateFinder(IJournalService journal) => _journal = journal;

    public Task<DuplicateScanResult> ScanAsync(DuplicateFinderOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Scan(options, progress, ct), ct);

    private static DuplicateScanResult Scan(DuplicateFinderOptions options,
        IProgress<SortProgress>? progress, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DuplicateScanResult();

        HashSet<string>? extensions = options.Extensions.Count > 0
            ? new HashSet<string>(options.Extensions, StringComparer.OrdinalIgnoreCase)
            : options.MediaOnly ? ToolsCommon.MediaExtensions : null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // folders may overlap
        var candidates = new List<(string Path, long Size)>();
        foreach (var folder in options.Folders)
        {
            foreach (var file in ToolsCommon.EnumerateFilesSafe(folder, options.Recursive))
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(file)) continue;
                if (extensions is not null && !extensions.Contains(Path.GetExtension(file))) continue;
                long size;
                try { size = new FileInfo(file).Length; } catch { continue; }
                if (size < options.MinFileSizeBytes) continue;
                candidates.Add((file, size));
                if (candidates.Count % 200 == 0)
                    progress?.Report(ToolsCommon.MakeProgress(file, candidates.Count, 0, 0, 0, stopwatch));
            }
        }
        result.FilesScanned = candidates.Count;

        var sizeGroups = candidates.GroupBy(c => c.Size).Where(g => g.Count() > 1).ToList();
        switch (options.CompareMode)
        {
            case DuplicateCompareMode.SizeOnly:
                foreach (var group in sizeGroups)
                    result.Groups.Add(new DuplicateGroup
                    {
                        Key = SizeFormatter.Format(group.Key),
                        FileSizeBytes = group.Key,
                        Files = [.. group.Select(c => c.Path)],
                    });
                break;

            case DuplicateCompareMode.NameAndSize:
                foreach (var group in sizeGroups)
                    foreach (var sub in group
                        .GroupBy(c => Path.GetFileName(c.Path), StringComparer.OrdinalIgnoreCase)
                        .Where(s => s.Count() > 1))
                        result.Groups.Add(new DuplicateGroup
                        {
                            Key = $"{SizeFormatter.Format(group.Key)} : {sub.Key}",
                            FileSizeBytes = group.Key,
                            Files = [.. sub.Select(c => c.Path)],
                        });
                break;

            default:
                HashGroups(sizeGroups, options.CompareMode == DuplicateCompareMode.FullHash,
                    result, progress, stopwatch, ct);
                break;
        }

        result.Groups.Sort((a, b) => b.WastedBytes.CompareTo(a.WastedBytes));
        result.Elapsed = stopwatch.Elapsed;
        return result;
    }

    private static void HashGroups(List<IGrouping<long, (string Path, long Size)>> sizeGroups,
        bool fullHash, DuplicateScanResult result, IProgress<SortProgress>? progress,
        Stopwatch stopwatch, CancellationToken ct)
    {
        int totalToHash = sizeGroups.Sum(g => g.Count());
        int hashed = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            foreach (var group in sizeGroups)
            {
                var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var (path, size) in group)
                {
                    ct.ThrowIfCancellationRequested();
                    string hash;
                    try
                    {
                        long bytesRead;
                        (hash, bytesRead) = fullHash
                            ? FullHash(path, buffer, ct)
                            : QuickHash(path, size, buffer, ct);
                        result.BytesHashed += bytesRead;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { continue; } // unreadable: leave it out of every group
                    hashed++;
                    if (!byHash.TryGetValue(hash, out var list)) byHash[hash] = list = [];
                    list.Add(path);
                    progress?.Report(ToolsCommon.MakeProgress(path, hashed, totalToHash,
                        result.BytesHashed, 0, stopwatch));
                }
                foreach (var (hash, files) in byHash)
                    if (files.Count > 1)
                        result.Groups.Add(new DuplicateGroup
                        {
                            Key = $"{SizeFormatter.Format(group.Key)} : {hash[..12].ToLowerInvariant()}",
                            FileSizeBytes = group.Key,
                            Files = files,
                        });
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>SHA-256 of (first 64 KiB + last 64 KiB + length); whole file when it is that small.</summary>
    private static (string Hash, long BytesRead) QuickHash(string path, long length, byte[] buffer, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            ChunkSize, FileOptions.SequentialScan);
        long bytesRead = 0;
        if (length <= 2L * ChunkSize)
        {
            int read;
            while ((read = fs.Read(buffer, 0, ChunkSize)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
                bytesRead += read;
            }
        }
        else
        {
            bytesRead += AppendBlock(fs, buffer, ChunkSize, sha);
            fs.Seek(length - ChunkSize, SeekOrigin.Begin);
            bytesRead += AppendBlock(fs, buffer, ChunkSize, sha);
        }
        sha.AppendData(BitConverter.GetBytes(length));
        return (Convert.ToHexString(sha.GetHashAndReset()), bytesRead);
    }

    private static (string Hash, long BytesRead) FullHash(string path, byte[] buffer, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            buffer.Length, FileOptions.SequentialScan);
        long bytesRead = 0;
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, read);
            bytesRead += read;
        }
        return (Convert.ToHexString(sha.GetHashAndReset()), bytesRead);
    }

    private static int AppendBlock(FileStream fs, byte[] buffer, int count, IncrementalHash sha)
    {
        int total = 0;
        while (total < count)
        {
            int read = fs.Read(buffer, 0, count - total);
            if (read == 0) break;
            sha.AppendData(buffer, 0, read);
            total += read;
        }
        return total;
    }

    public async Task<SortResult> ResolveAsync(DuplicateScanResult scan, DuplicateFinderOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default)
    {
        if (options.Resolution == DuplicateResolution.ReportOnly)
            return new SortResult { DestinationRoot = options.MoveToFolder };

        if (options.Resolution == DuplicateResolution.MoveToFolder && string.IsNullOrWhiteSpace(options.MoveToFolder))
        {
            var failed = new SortResult();
            failed.Errors.Add(("", "No duplicates folder configured"));
            return failed;
        }

        // "Delete" is a journaled soft-delete: hard deletes cannot be undone, so the file is
        // moved into this journal's backup folder and recorded with DisplacedBackupPath —
        // History → Undo restores it. Nothing here ever hard-deletes.
        var journalId = Guid.NewGuid();
        var backupFolder = Path.Combine(_journal.JournalDirectory, "Backups", journalId.ToString("N"));
        bool softDelete = options.Resolution == DuplicateResolution.Delete;
        var destFolder = softDelete ? backupFolder : options.MoveToFolder;

        var result = new SortResult { DestinationRoot = destFolder };
        var journal = new SortJournal
        {
            Id = journalId,
            TimestampUtc = DateTime.UtcNow,
            OperationKind = "Duplicate move",
            SourceFolder = options.Folders.FirstOrDefault() ?? "",
            DestinationRoot = destFolder,
            Action = SortAction.Move,
            BackupFolder = softDelete ? backupFolder : null,
        };

        var stopwatch = Stopwatch.StartNew();
        var checkpoint = new JournalCheckpoint(_journal, journal);
        await Task.Run(() =>
        {
            int total = scan.Groups.Sum(g => Math.Max(0, g.Files.Count - 1));
            int done = 0;
            long bytes = 0;
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool TargetTaken(string p) => claimed.Contains(p) || File.Exists(p) || Directory.Exists(p);

            foreach (var group in scan.Groups)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
                if (group.Files.Count < 2) continue;
                var keeper = PickKeeper(group.Files, options.KeepPolicy);
                foreach (var file in group.Files)
                {
                    if (ct.IsCancellationRequested) { result.Cancelled = true; break; }
                    if (string.Equals(file, keeper, StringComparison.Ordinal)) continue;
                    try
                    {
                        if (!File.Exists(file))
                        {
                            result.Errors.Add((file, "File no longer exists"));
                            continue;
                        }
                        if (!Directory.Exists(destFolder))
                        {
                            // Record every level CreateDirectory brings into being (deepest
                            // first), so undo can prune the whole created chain, not just the leaf.
                            var missing = new List<string>();
                            var level = destFolder;
                            while (!string.IsNullOrEmpty(level) && !Directory.Exists(level))
                            {
                                missing.Add(level);
                                level = Path.GetDirectoryName(level);
                            }
                            Directory.CreateDirectory(destFolder);
                            journal.CreatedDirectories.AddRange(missing);
                        }
                        var dest = PathSanitizer.MakeUnique(
                            Path.Combine(destFolder, Path.GetFileName(file)), TargetTaken);
                        long size = new FileInfo(file).Length;
                        File.Move(file, dest);
                        claimed.Add(dest);
                        journal.Entries.Add(new JournalEntry
                        {
                            Operation = JournalOperation.MovedToDuplicates,
                            OriginalPath = file,
                            NewPath = dest,
                            DisplacedBackupPath = softDelete ? dest : null,
                            SizeBytes = size,
                        });
                        result.Processed++;
                        result.DuplicatesDiverted++;
                        if (!softDelete) result.Moved++;
                        bytes += size;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add((file, ex.Message));
                    }
                    done++;
                    progress?.Report(ToolsCommon.MakeProgress(file, done, total, bytes, 0, stopwatch));
                    // A crash mid-resolution must not leave soft-deleted files hidden in the
                    // backup folder with no journal on disk pointing at them.
                    checkpoint.MaybeSave();
                }
            }
        }, CancellationToken.None);

        if (journal.Entries.Count > 0)
        {
            await _journal.SaveAsync(journal, CancellationToken.None);
            result.JournalPath = ToolsCommon.JournalFilePath(_journal, journal);
        }
        result.Elapsed = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Which file in a duplicate group the resolution keeps, per the keep policy.
    /// Public so the UI can show exactly the keeper the engine will honor.
    /// </summary>
    public static string PickKeeper(IReadOnlyList<string> files, DuplicateKeepPolicy policy) => policy switch
    {
        DuplicateKeepPolicy.Oldest => files.OrderBy(EffectiveDate)
            .ThenBy(f => f, StringComparer.Ordinal).First(),
        DuplicateKeepPolicy.Newest => files.OrderByDescending(EffectiveDate)
            .ThenBy(f => f, StringComparer.Ordinal).First(),
        DuplicateKeepPolicy.ShortestPath => files.OrderBy(f => f.Length)
            .ThenBy(f => f, StringComparer.Ordinal).First(),
        DuplicateKeepPolicy.LongestPath => files.OrderByDescending(f => f.Length)
            .ThenBy(f => f, StringComparer.Ordinal).First(),
        _ => files.OrderBy(f => f, StringComparer.Ordinal).First(),
    };

    /// <summary>A file's age for keep-policy purposes: the earlier of created and modified.</summary>
    private static DateTime EffectiveDate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.CreationTime < info.LastWriteTime ? info.CreationTime : info.LastWriteTime;
        }
        catch
        {
            return DateTime.MaxValue;
        }
    }
}
