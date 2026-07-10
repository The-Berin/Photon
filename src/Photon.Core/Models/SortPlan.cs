namespace Photon.Core.Models;

/// <summary>One planned file operation: where a scanned file will land.</summary>
public sealed class SortPlanItem
{
    public required MediaFile Source { get; init; }
    /// <summary>Full destination path decided by the planner (before run-time collision handling).</summary>
    public required string PlannedDestination { get; set; }
    /// <summary>True when the planner already knows this file duplicates another planned file's content.</summary>
    public bool IsContentDuplicate { get; set; }
    /// <summary>Date the planner used, null when the file goes to the unknown-date folder.</summary>
    public DateTime? ResolvedDate { get; set; }
    public bool DateFromExif { get; set; }
}

/// <summary>The full dry-run result: every planned operation plus space accounting.</summary>
public sealed class SortPlan
{
    public required string DestinationRoot { get; init; }
    public List<SortPlanItem> Items { get; init; } = [];
    public long TotalBytes { get; init; }
    /// <summary>Bytes of new data the destination volume must absorb (0 for same-volume moves).</summary>
    public long RequiredBytes { get; init; }
    public long DestinationFreeBytes { get; init; }
    public List<string> Warnings { get; init; } = [];

    /// <summary>Feature 8: refuse to start unless the destination can hold everything plus a safety margin.</summary>
    public const long SafetyMarginBytes = 200L * 1024 * 1024;
    public bool HasEnoughSpace => DestinationFreeBytes >= RequiredBytes + SafetyMarginBytes;
}

/// <summary>Progress snapshot pushed to the UI during scans, sorts, undo, and tools runs.</summary>
public sealed class SortProgress
{
    public string CurrentFile { get; init; } = "";
    public int ProcessedCount { get; init; }
    public int TotalCount { get; init; }
    public long ProcessedBytes { get; init; }
    public long TotalBytes { get; init; }
    public double FilesPerSecond { get; init; }
    public double BytesPerSecond { get; init; }
    public TimeSpan? EstimatedRemaining { get; init; }
}

/// <summary>Outcome of an executed sort.</summary>
public sealed class SortResult
{
    public int Processed { get; set; }
    public int Copied { get; set; }
    public int Moved { get; set; }
    public int RenamedOnCollision { get; set; }
    public int Overwrote { get; set; }
    public int SkippedExisting { get; set; }
    public int DuplicatesDiverted { get; set; }
    public List<(string File, string Error)> Errors { get; } = [];
    public string DestinationRoot { get; set; } = "";
    public string? JournalPath { get; set; }
    public string? LogFilePath { get; set; }
    public string? CsvPath { get; set; }
    public TimeSpan Elapsed { get; set; }
    public bool Cancelled { get; set; }
}
