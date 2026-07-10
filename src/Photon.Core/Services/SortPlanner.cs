using System.Globalization;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>Default <see cref="ISortPlanner"/>: builds the dry-run plan with space accounting.</summary>
public sealed class SortPlanner : ISortPlanner
{
    private readonly IMetadataReader _metadata;
    private readonly IDateResolver _dates;

    public SortPlanner() : this(new MetadataReader(), new DateResolver()) { }

    public SortPlanner(IMetadataReader metadata, IDateResolver dates)
    {
        _metadata = metadata;
        _dates = dates;
    }

    public Task<SortPlan> BuildPlanAsync(IReadOnlyList<MediaFile> files, SortOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => BuildPlan(files, options, progress, ct), ct);

    private SortPlan BuildPlan(IReadOnlyList<MediaFile> files, SortOptions options,
        IProgress<SortProgress>? progress, CancellationToken ct)
    {
        var warnings = new List<string>();
        var destRoot = Path.GetFullPath(options.ResolveOutputFolder());

        // A re-sort of a folder containing previous sorted output must not re-sort that output.
        // But when the destination IS the source folder (or an ancestor of it — an in-place
        // sort), the prefix test would exclude every scanned file and silently no-op the run,
        // so the exclusion is skipped entirely in that case.
        var destPrefix = destRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        var sortInPlace = false;
        try
        {
            sortInPlace = !string.IsNullOrWhiteSpace(options.SourceFolder)
                && (Path.GetFullPath(options.SourceFolder)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar)
                    .StartsWith(destPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch { /* unparseable source folder: keep the exclusion */ }

        var candidates = new List<MediaFile>(files.Count);
        var excluded = 0;
        foreach (var file in files)
        {
            if (!sortInPlace && Path.GetFullPath(file.FilePath).StartsWith(destPrefix, StringComparison.OrdinalIgnoreCase))
                excluded++;
            else
                candidates.Add(file);
        }
        if (excluded > 0)
            warnings.Add($"{excluded} file(s) already inside the destination folder \"{destRoot}\" were excluded from this sort.");

        // The CSV summary promises camera/gps columns, so it needs metadata too.
        var needMetadata = options.GroupByCamera || options.ExportCsvSummary
                           || options.DateSource != DateSource.FileDateOnly;
        var items = new List<SortPlanItem>(candidates.Count);
        var noDateCount = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = candidates[i];
            if (needMetadata && !file.MetadataLoaded) _metadata.Populate(file);

            var resolution = _dates.Resolve(file, options.DateSource);
            if (resolution.Date is null) noDateCount++;

            items.Add(new SortPlanItem
            {
                Source = file,
                PlannedDestination = BuildDestination(destRoot, file, resolution.Date, options),
                ResolvedDate = resolution.Date,
                DateFromExif = resolution.FromExif,
            });

            if ((i + 1) % 25 == 0 || i + 1 == candidates.Count)
                progress?.Report(new SortProgress
                {
                    CurrentFile = file.FilePath,
                    ProcessedCount = i + 1,
                    TotalCount = candidates.Count,
                });
        }

        if (noDateCount > 0 && options.DateSource == DateSource.ExifOnly)
            warnings.Add($"{noDateCount} file(s) have no EXIF date and will be placed in \"{options.UnknownDateFolderName}\".");

        if (options.DetectExactDuplicates)
            MarkContentDuplicates(items, warnings, progress, ct);

        long totalBytes = items.Sum(it => it.Source.SizeBytes);

        return new SortPlan
        {
            DestinationRoot = destRoot,
            Items = items,
            TotalBytes = totalBytes,
            RequiredBytes = ComputeRequiredBytes(items, destRoot, options.Action, totalBytes),
            DestinationFreeBytes = GetDestinationFreeBytes(destRoot, warnings),
            Warnings = warnings,
        };
    }

    private static string BuildDestination(string destRoot, MediaFile file, DateTime? date, SortOptions options)
    {
        if (date is null)
            return Path.Combine(destRoot,
                PathSanitizer.SanitizeSegment(options.UnknownDateFolderName, "Unknown Date"),
                file.FileName);

        var d = date.Value;
        var dir = Path.Combine(destRoot, d.Year.ToString("0000", CultureInfo.InvariantCulture));
        if (options.Structure is FolderStructure.YearMonth or FolderStructure.YearMonthDay)
            dir = Path.Combine(dir, options.MonthFormat == MonthFormat.Name
                ? d.ToString("MMMM", CultureInfo.InvariantCulture)
                : d.ToString("MM", CultureInfo.InvariantCulture));
        if (options.Structure is FolderStructure.YearMonthDay)
            dir = Path.Combine(dir, d.ToString("dd", CultureInfo.InvariantCulture));
        if (options.IncludeTimeSubfolder)
            dir = Path.Combine(dir, d.ToString("HH-mm", CultureInfo.InvariantCulture));

        if (options.GroupByCamera)
        {
            if (string.IsNullOrWhiteSpace(file.CameraMake) && string.IsNullOrWhiteSpace(file.CameraModel))
            {
                dir = Path.Combine(dir, "Unknown Camera");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(file.CameraMake))
                    dir = Path.Combine(dir, PathSanitizer.SanitizeSegment(file.CameraMake));
                if (!string.IsNullOrWhiteSpace(file.CameraModel))
                    dir = Path.Combine(dir, PathSanitizer.SanitizeSegment(file.CameraModel));
            }
        }

        return Path.Combine(dir, file.FileName);
    }

    private static void MarkContentDuplicates(List<SortPlanItem> items, List<string> warnings,
        IProgress<SortProgress>? progress, CancellationToken ct)
    {
        // Size prefilter: only files sharing a size ever get hashed.
        var sizeGroups = items.GroupBy(it => it.Source.SizeBytes).Where(g => g.Count() > 1).ToList();
        var totalToHash = sizeGroups.Sum(g => g.Count());
        long bytesToHash = sizeGroups.Sum(g => g.Key * g.Count());
        var hashedFiles = 0;
        long hashedBytes = 0;

        foreach (var group in sizeGroups)
        {
            var firstByHash = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in group) // GroupBy preserves plan order, so "later copies" get marked
            {
                ct.ThrowIfCancellationRequested();
                string hash;
                try
                {
                    hash = Hashing.ComputeSha256(item.Source.FilePath, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not hash \"{item.Source.FilePath}\" for duplicate detection: {ex.Message}");
                    continue;
                }

                if (!firstByHash.Add(hash)) item.IsContentDuplicate = true;

                hashedFiles++;
                hashedBytes += item.Source.SizeBytes;
                progress?.Report(new SortProgress
                {
                    CurrentFile = item.Source.FilePath,
                    ProcessedCount = hashedFiles,
                    TotalCount = totalToHash,
                    ProcessedBytes = hashedBytes,
                    TotalBytes = bytesToHash,
                });
            }
        }
    }

    private static long ComputeRequiredBytes(List<SortPlanItem> items, string destRoot, SortAction action, long totalBytes)
    {
        if (action == SortAction.Copy) return totalBytes;

        // A same-volume move consumes no new space; only cross-volume moves do.
        var destVolume = SafeVolumeRoot(destRoot);
        long required = 0;
        foreach (var item in items)
        {
            var srcVolume = SafeVolumeRoot(item.Source.FilePath);
            if (!string.Equals(srcVolume, destVolume, StringComparison.OrdinalIgnoreCase))
                required += item.Source.SizeBytes;
        }
        return required;
    }

    private static string? SafeVolumeRoot(string path)
    {
        try { return Path.GetPathRoot(Path.GetFullPath(path)); }
        catch { return null; }
    }

    private static long GetDestinationFreeBytes(string destRoot, List<string> warnings)
    {
        try
        {
            var root = Path.GetPathRoot(destRoot);
            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException("The destination has no volume root.");
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not determine free space on the destination volume for \"{destRoot}\" ({ex.Message}); the space check is skipped.");
            return long.MaxValue;
        }
    }
}
