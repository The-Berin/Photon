namespace Photon.Core.Models;

/// <summary>Which files a scan picks up.</summary>
public sealed class ScanFilter
{
    /// <summary>Extensions with leading dot, lower-case, e.g. [".jpg", ".png"].</summary>
    public required HashSet<string> PictureExtensions { get; init; }
    public required HashSet<string> VideoExtensions { get; init; }
    public bool Recursive { get; init; } = true;

    public static readonly string[] DefaultPictureExtensions =
        [".jpg", ".jpeg", ".png", ".heic", ".heif", ".gif", ".bmp", ".tif", ".tiff", ".webp",
         ".dng", ".raw", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf"];
    public static readonly string[] DefaultVideoExtensions =
        [".mp4", ".mov", ".avi", ".mkv", ".m4v", ".wmv", ".mts", ".m2ts", ".3gp", ".webm", ".mpg", ".mpeg"];

    /// <summary>Builds the filter a SortOptions implies (custom extensions override the type flags).</summary>
    public static ScanFilter FromSortOptions(SortOptions o)
    {
        // Users type "*.jpg", "jpg." or "tar.gz"; Path.GetExtension only ever yields the
        // final ".xyz" segment, so anything else would silently match zero files.
        var custom = (o.CustomExtensions ?? "")
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e =>
            {
                var cleaned = e.Replace("*", "").Trim('.', ' ');
                var lastDot = cleaned.LastIndexOf('.');
                return lastDot >= 0 ? cleaned[(lastDot + 1)..] : cleaned;
            })
            .Where(e => e.Length > 0)
            .Select(e => "." + e.ToLowerInvariant())
            .ToHashSet();
        if (custom.Count > 0)
        {
            // Known video extensions keep their video classification so the metadata
            // reader uses container dates instead of EXIF for them.
            var videos = custom.Where(DefaultVideoExtensions.Contains).ToHashSet();
            custom.ExceptWith(videos);
            return new ScanFilter { PictureExtensions = custom, VideoExtensions = videos, Recursive = o.IncludeSubfolders };
        }
        return new ScanFilter
        {
            PictureExtensions = o.IncludePictures ? [.. DefaultPictureExtensions] : [],
            VideoExtensions = o.IncludeVideos ? [.. DefaultVideoExtensions] : [],
            Recursive = o.IncludeSubfolders,
        };
    }
}

/// <summary>Feature 2: quick folder/drive scan report with sort estimates.</summary>
public sealed class FolderScanReport
{
    public required string Root { get; init; }
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public int TotalFolders { get; set; }
    public int PictureCount { get; set; }
    public long PictureBytes { get; set; }
    public int VideoCount { get; set; }
    public long VideoBytes { get; set; }
    public int OtherCount { get; set; }
    public long OtherBytes { get; set; }
    /// <summary>Extension (with dot) → (count, bytes), sorted by bytes descending when displayed.</summary>
    public Dictionary<string, (int Count, long Bytes)> ByExtension { get; init; } = [];
    public int MaxDepth { get; set; }
    public string? LargestFilePath { get; set; }
    public long LargestFileBytes { get; set; }
    public DateTime? OldestFileDate { get; set; }
    public DateTime? NewestFileDate { get; set; }
    public int InaccessibleItems { get; set; }
    /// <summary>Rough sort-time estimate assuming the given throughput.</summary>
    public TimeSpan EstimateSortTime(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((PictureBytes + VideoBytes) / bytesPerSecond);
    public TimeSpan ScanDuration { get; set; }
}

/// <summary>Options for the standalone duplicate finder (feature 4).</summary>
public sealed class DuplicateFinderOptions
{
    public List<string> Folders { get; set; } = [];
    public bool Recursive { get; set; } = true;
    public DuplicateCompareMode CompareMode { get; set; } = DuplicateCompareMode.QuickHash;
    public long MinFileSizeBytes { get; set; }
    /// <summary>Empty = every file; otherwise extensions with dot, lower-case.</summary>
    public HashSet<string> Extensions { get; set; } = [];
    public bool MediaOnly { get; set; } = true;
    public DuplicateResolution Resolution { get; set; } = DuplicateResolution.ReportOnly;
    public DuplicateKeepPolicy KeepPolicy { get; set; } = DuplicateKeepPolicy.Oldest;
    public string MoveToFolder { get; set; } = "";
}

/// <summary>A set of files with identical content (or matching per the compare mode).</summary>
public sealed class DuplicateGroup
{
    public required string Key { get; init; }
    public long FileSizeBytes { get; init; }
    public List<string> Files { get; init; } = [];
    /// <summary>Bytes reclaimable if all but one copy were removed.</summary>
    public long WastedBytes => FileSizeBytes * Math.Max(0, Files.Count - 1);
}

public sealed class DuplicateScanResult
{
    public List<DuplicateGroup> Groups { get; init; } = [];
    public int FilesScanned { get; set; }
    public long BytesHashed { get; set; }
    public TimeSpan Elapsed { get; set; }
    public long TotalWastedBytes => Groups.Sum(g => g.WastedBytes);
}

/// <summary>Options for folder flattening (feature 3).</summary>
public sealed class FlattenOptions
{
    public required string Root { get; init; }
    /// <summary>Only flatten media files; leave others where they are.</summary>
    public bool MediaOnly { get; set; }
    public FlattenConflictPolicy ConflictPolicy { get; set; } = FlattenConflictPolicy.AppendNumber;
    public bool RemoveEmptyFolders { get; set; } = true;
}

public sealed class FlattenPlanItem
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; set; }
}

public sealed class FlattenPlan
{
    public required string Root { get; init; }
    public List<FlattenPlanItem> Items { get; init; } = [];
    public int FoldersToRemove { get; set; }
    public List<string> Warnings { get; init; } = [];
}

public sealed class FlattenResult
{
    public int Moved { get; set; }
    public int Skipped { get; set; }
    public int FoldersRemoved { get; set; }
    public List<(string File, string Error)> Errors { get; } = [];
    public string? JournalPath { get; set; }
}

/// <summary>Feature 10: one physical/logical drive's vitals for the Drive Inspector.</summary>
public sealed class DriveReport
{
    public required string Name { get; init; }          // e.g. "C:\"
    public string VolumeLabel { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public string MediaType { get; set; } = "Unknown";  // SSD / HDD / Removable / Unknown
    public string BusType { get; set; } = "";           // NVMe / SATA / USB / ...
    public string HealthStatus { get; set; } = "";      // Healthy / Warning / Unhealthy (from Storage WMI)
    public string PhysicalModel { get; set; } = "";
    // Benchmark results (null until a speed test runs)
    public double? SequentialReadMBps { get; set; }
    public double? SequentialWriteMBps { get; set; }
    public string? BenchmarkNote { get; set; }
}
