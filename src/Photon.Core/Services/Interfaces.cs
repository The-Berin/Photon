using Photon.Core.Models;

namespace Photon.Core.Services;

/// <summary>Enumerates media files under a folder per the filter. Never throws on per-file access errors; skips and counts them.</summary>
public interface IFileScanner
{
    Task<List<MediaFile>> ScanAsync(string folder, ScanFilter filter,
        IProgress<int>? filesFound = null, CancellationToken ct = default);
}

/// <summary>Reads EXIF / video-container metadata (date taken, camera, GPS, dimensions) into a MediaFile.</summary>
public interface IMetadataReader
{
    /// <summary>Fills the EXIF fields; sets MetadataLoaded. Must never throw — a corrupt file just leaves fields null.</summary>
    void Populate(MediaFile file);
}

/// <summary>Applies a DateSource policy to a (metadata-populated) file.</summary>
public interface IDateResolver
{
    DateResolution Resolve(MediaFile file, DateSource source);
}

/// <summary>Builds the dry-run plan: destination path for every file, plus space accounting (feature 8).</summary>
public interface ISortPlanner
{
    Task<SortPlan> BuildPlanAsync(IReadOnlyList<MediaFile> files, SortOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default);
}

/// <summary>
/// Executes a plan: copies/moves with collision handling, hashing for duplicate detection,
/// journal recording, log/CSV writing, progress reporting, and clean cancellation
/// (never leaves a half-written destination file behind).
/// </summary>
public interface ISortExecutor
{
    Task<SortResult> ExecuteAsync(SortPlan plan, SortOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default);
}

/// <summary>Persists journals and performs undo (feature 5).</summary>
public interface IJournalService
{
    string JournalDirectory { get; }
    Task SaveAsync(SortJournal journal, CancellationToken ct = default);
    /// <summary>All saved journals, newest first.</summary>
    List<SortJournal> LoadAll();
    SortJournal? LoadLatestUndoable();
    /// <summary>Reverses every entry (moves back, deletes copies, restores overwrite backups), prunes created-then-empty dirs, marks the journal undone.</summary>
    Task<UndoResult> UndoAsync(SortJournal journal,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default);
}

/// <summary>Batch rename engine (feature 1). Pure planning + journaled execution.</summary>
public interface IRenameEngine
{
    /// <summary>Applies the full pipeline to produce preview rows. Never touches disk.</summary>
    List<RenamePlanItem> BuildPlan(IReadOnlyList<string> files, RenameOptions options);
    Task<RenameResult> ExecuteAsync(IReadOnlyList<RenamePlanItem> plan, RenameOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default);
}

/// <summary>Standalone duplicate finder (feature 4).</summary>
public interface IDuplicateFinder
{
    Task<DuplicateScanResult> ScanAsync(DuplicateFinderOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default);
    /// <summary>Applies the resolution (move/delete) keeping one file per group per the keep policy. Journaled when moving.</summary>
    Task<SortResult> ResolveAsync(DuplicateScanResult scan, DuplicateFinderOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default);
}

/// <summary>Folder flattener (feature 3). Journaled.</summary>
public interface IFolderFlattener
{
    Task<FlattenPlan> BuildPlanAsync(FlattenOptions options, CancellationToken ct = default);
    Task<FlattenResult> ExecuteAsync(FlattenPlan plan, FlattenOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default);
}

/// <summary>Quick folder/drive scanner (feature 2).</summary>
public interface IFolderScanner
{
    Task<FolderScanReport> ScanAsync(string root,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default);
}
