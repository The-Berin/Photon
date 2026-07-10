namespace Photon.Core.Models;

/// <summary>
/// One recorded file operation. The journal is the undo system's source of truth:
/// every entry must contain enough information to reverse the operation exactly.
/// </summary>
public sealed class JournalEntry
{
    public required JournalOperation Operation { get; init; }
    /// <summary>Where the file was before the operation.</summary>
    public required string OriginalPath { get; init; }
    /// <summary>Where the file ended up (null for SkippedExisting).</summary>
    public string? NewPath { get; init; }
    /// <summary>
    /// For Overwrote: where the displaced destination file was stashed
    /// (under the journal's backup folder) so undo can restore it.
    /// </summary>
    public string? DisplacedBackupPath { get; init; }
    public long SizeBytes { get; init; }
}

/// <summary>
/// A complete record of one sort / rename / flatten run, saved as JSON in
/// %APPDATA%\Photon\Journals. Directories created by the run are recorded so
/// undo can remove the ones it leaves empty.
/// </summary>
public sealed class SortJournal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; init; }
    /// <summary>"Sort", "Batch rename", "Flatten", "Preview sort", "Duplicate move" — shown in the History UI.</summary>
    public required string OperationKind { get; init; }
    public required string SourceFolder { get; init; }
    public required string DestinationRoot { get; init; }
    public SortAction Action { get; init; }
    public List<JournalEntry> Entries { get; init; } = [];
    /// <summary>Directories the run created, deepest first, so undo can prune empty ones.</summary>
    public List<string> CreatedDirectories { get; init; } = [];
    /// <summary>Folder holding files displaced by Overwrite, for restoration on undo.</summary>
    public string? BackupFolder { get; init; }
    public bool UndoneUtc => UndoneAtUtc is not null;
    public DateTime? UndoneAtUtc { get; set; }
}

/// <summary>Outcome of undoing a journal.</summary>
public sealed class UndoResult
{
    public int Reversed { get; set; }
    public int RestoredFromBackup { get; set; }
    public int DirectoriesRemoved { get; set; }
    public List<(string File, string Error)> Errors { get; } = [];
    public bool Cancelled { get; set; }
}
