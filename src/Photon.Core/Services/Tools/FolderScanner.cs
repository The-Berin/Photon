using System.Diagnostics;
using Photon.Core.Models;

namespace Photon.Core.Services;

/// <summary>
/// Feature 2: quick folder/drive statistics. A single pass with no hashing and no metadata
/// reads — it must stay fast enough to point at a whole drive.
/// </summary>
public sealed class FolderScanner : IFolderScanner
{
    private static readonly HashSet<string> PictureExtensions =
        new(ScanFilter.DefaultPictureExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VideoExtensions =
        new(ScanFilter.DefaultVideoExtensions, StringComparer.OrdinalIgnoreCase);

    public Task<FolderScanReport> ScanAsync(string root,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Scan(root, progress, ct), ct);

    private static FolderScanReport Scan(string root, IProgress<SortProgress>? progress, CancellationToken ct)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Folder not found: {root}");

        var report = new FolderScanReport { Root = root };
        var stopwatch = Stopwatch.StartNew();
        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = pending.Pop();
            if (depth > report.MaxDepth) report.MaxDepth = depth;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { report.InaccessibleItems++; subs = []; }
            foreach (var sub in subs)
            {
                report.TotalFolders++;
                pending.Push((sub, depth + 1));
            }

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { report.InaccessibleItems++; continue; }
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                long size;
                DateTime modified;
                try
                {
                    var info = new FileInfo(file);
                    size = info.Length;
                    modified = info.LastWriteTime;
                }
                catch
                {
                    report.InaccessibleItems++;
                    continue;
                }

                report.TotalFiles++;
                report.TotalBytes += size;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                report.ByExtension[ext] = report.ByExtension.TryGetValue(ext, out var agg)
                    ? (agg.Count + 1, agg.Bytes + size)
                    : (1, size);

                if (PictureExtensions.Contains(ext)) { report.PictureCount++; report.PictureBytes += size; }
                else if (VideoExtensions.Contains(ext)) { report.VideoCount++; report.VideoBytes += size; }
                else { report.OtherCount++; report.OtherBytes += size; }

                if (size > report.LargestFileBytes)
                {
                    report.LargestFileBytes = size;
                    report.LargestFilePath = file;
                }
                if (report.OldestFileDate is null || modified < report.OldestFileDate) report.OldestFileDate = modified;
                if (report.NewestFileDate is null || modified > report.NewestFileDate) report.NewestFileDate = modified;

                if (report.TotalFiles % 100 == 0)
                    progress?.Report(ToolsCommon.MakeProgress(file, report.TotalFiles, 0,
                        report.TotalBytes, 0, stopwatch));
            }
        }

        report.ScanDuration = stopwatch.Elapsed;
        progress?.Report(ToolsCommon.MakeProgress("", report.TotalFiles, report.TotalFiles,
            report.TotalBytes, report.TotalBytes, stopwatch));
        return report;
    }
}
