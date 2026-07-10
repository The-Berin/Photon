namespace Photon.Core.Models;

/// <summary>How the destination folder tree is shaped.</summary>
public enum FolderStructure
{
    YearOnly,
    YearMonth,
    YearMonthDay,
}

/// <summary>How month folders are named: "03" vs "March".</summary>
public enum MonthFormat
{
    Number,
    Name,
}

/// <summary>Where the date used for sorting comes from.</summary>
public enum DateSource
{
    ExifThenFileDate,
    ExifOnly,
    FileDateThenExif,
    FileDateOnly,
}

/// <summary>What to do when the destination file name already exists.</summary>
public enum DuplicateHandling
{
    /// <summary>Append _1, _2, ... to the new file's name.</summary>
    Rename,
    /// <summary>Leave the source file unprocessed.</summary>
    Skip,
    /// <summary>Replace the destination file (the displaced file is preserved in the journal backup for undo).</summary>
    Overwrite,
}

/// <summary>Whether the sort copies or moves files.</summary>
public enum SortAction
{
    Copy,
    Move,
}

/// <summary>What the app does after a sort finishes.</summary>
public enum WhenDoneAction
{
    DoNothing,
    OpenOutputFolder,
    CloseApp,
    Sleep,
    Shutdown,
}

/// <summary>Display unit for byte counts.</summary>
public enum SizeUnit
{
    Auto,
    Bytes,
    KB,
    MB,
    GB,
    TB,
}

/// <summary>Theme preference.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Outcome recorded for one file operation in a journal.</summary>
public enum JournalOperation
{
    Copied,
    Moved,
    Overwrote,
    RenamedInPlace,
    SkippedExisting,
    MovedToDuplicates,
    LinkCreated,
}

/// <summary>How the duplicate finder decides two files match.</summary>
public enum DuplicateCompareMode
{
    /// <summary>Same size only (fast, loose).</summary>
    SizeOnly,
    /// <summary>Same size, then same hash of first+last 64 KB (fast, accurate for media).</summary>
    QuickHash,
    /// <summary>Same size, then same full SHA-256 (slow, exact).</summary>
    FullHash,
    /// <summary>Same file name AND same size.</summary>
    NameAndSize,
}

/// <summary>What to do with confirmed duplicates.</summary>
public enum DuplicateResolution
{
    ReportOnly,
    MoveToFolder,
    Delete,
}

/// <summary>Which copy in a duplicate group is kept as the original.</summary>
public enum DuplicateKeepPolicy
{
    Oldest,
    Newest,
    ShortestPath,
    LongestPath,
    FirstAlphabetical,
}

/// <summary>Case transform applied by the batch renamer.</summary>
public enum CaseTransform
{
    None,
    Lower,
    Upper,
    TitleCase,
    SentenceCase,
    InvertCase,
}

/// <summary>What the batch renamer does when the target name already exists.</summary>
public enum RenameConflictPolicy
{
    AppendNumber,
    Skip,
    Fail,
}

/// <summary>What the folder flattener does with duplicate names while flattening.</summary>
public enum FlattenConflictPolicy
{
    AppendNumber,
    AppendFolderName,
    Skip,
}
