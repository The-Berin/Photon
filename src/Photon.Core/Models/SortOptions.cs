namespace Photon.Core.Models;

/// <summary>
/// Everything configurable about one sort run. Mirrors the three settings tabs in the UI.
/// Serialized to JSON for settings persistence, so keep it plain data.
/// </summary>
public sealed class SortOptions
{
    // ----- Folders -----
    public string SourceFolder { get; set; } = "";
    /// <summary>Null/empty means default to SourceFolder\Sorted.</summary>
    public string? OutputFolder { get; set; }

    /// <summary>The effective destination root for a sort run.</summary>
    public string ResolveOutputFolder() =>
        string.IsNullOrWhiteSpace(OutputFolder)
            ? Path.Combine(SourceFolder, "Sorted")
            : OutputFolder!;

    // ----- Structure & date tab -----
    public FolderStructure Structure { get; set; } = FolderStructure.YearMonthDay;
    public MonthFormat MonthFormat { get; set; } = MonthFormat.Name;
    /// <summary>Adds an HH-MM subfolder under the day folder (e.g. "14-30").</summary>
    public bool IncludeTimeSubfolder { get; set; }
    /// <summary>Adds Make\Model folders under the date folders.</summary>
    public bool GroupByCamera { get; set; }
    public DateSource DateSource { get; set; } = DateSource.ExifThenFileDate;
    /// <summary>Folder name used when no date can be resolved for a file.</summary>
    public string UnknownDateFolderName { get; set; } = "Unknown Date";

    // ----- Duplicates & files tab -----
    public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Rename;
    /// <summary>Hash files and divert exact content duplicates into the duplicates folder.</summary>
    public bool DetectExactDuplicates { get; set; }
    /// <summary>Put diverted duplicates into a "Duplicates" subfolder of the output root; off = detected duplicates sort normally.</summary>
    public bool MoveDuplicatesToSubfolder { get; set; } = true;
    public bool IncludePictures { get; set; } = true;
    public bool IncludeVideos { get; set; } = true;
    /// <summary>Space/comma/semicolon-separated list like "jpg png raw". Overrides the two flags above when non-empty.</summary>
    public string CustomExtensions { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
    public SortAction Action { get; set; } = SortAction.Copy;

    // ----- Log & export tab -----
    public bool WriteLogFile { get; set; }
    public bool ExportCsvSummary { get; set; }
    public string SoundWavPath { get; set; } = "";
    public bool KeepAwake { get; set; } = true;
    public SizeUnit SizeUnit { get; set; } = SizeUnit.Auto;
    public WhenDoneAction WhenDone { get; set; } = WhenDoneAction.DoNothing;

    public SortOptions Clone() => (SortOptions)MemberwiseClone();
}
