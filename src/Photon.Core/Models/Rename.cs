namespace Photon.Core.Models;

/// <summary>One find/replace step in the rename pipeline.</summary>
public sealed class FindReplaceRule
{
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
    public bool UseRegex { get; set; }
    public bool CaseSensitive { get; set; }
    /// <summary>Match whole words only (literal mode; regex rules ignore this).</summary>
    public bool WholeWord { get; set; }
    /// <summary>Replace only the first occurrence instead of all.</summary>
    public bool FirstOnly { get; set; }
    public ReplaceTarget Target { get; set; } = ReplaceTarget.NameOnly;
    public bool Enabled { get; set; } = true;
}

/// <summary>Insert fixed text into the name, at a position or relative to found text.</summary>
public sealed class InsertRule
{
    public string Text { get; set; } = "";
    public InsertAnchor Anchor { get; set; } = InsertAnchor.AtPosition;
    /// <summary>Character index for AtPosition.</summary>
    public int Position { get; set; }
    public bool FromEnd { get; set; }
    /// <summary>Substring to anchor on for BeforeText/AfterText (first occurrence).</summary>
    public string AnchorText { get; set; } = "";
    public bool Enabled { get; set; }
}

/// <summary>Remove a character range from the name.</summary>
public sealed class RemoveRangeRule
{
    public int Start { get; set; }
    public int Count { get; set; }
    public bool FromEnd { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>Remove everything between two delimiters (first From to next To).</summary>
public sealed class RemoveBetweenRule
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool IncludeDelimiters { get; set; } = true;
    public bool Enabled { get; set; }
}

/// <summary>
/// The batch renamer's full option set — the "NASA control center" panel binds to this.
///
/// Pipeline order: pattern expansion → swap-around-separator → find/replace steps →
/// removes (ranges, between-delimiters, camera prefixes, date/GUID/URL patterns, word list,
/// brackets, numbers, punctuation, non-ASCII/emoji, strip chars) → inserts →
/// hygiene (pad number runs, separator normalization, space replacement, diacritics /
/// transliteration, trim, truncate) → case transforms → affixes (prefix/suffix/parent
/// folder) → extension operations → Windows-safe sanitize → collision handling.
///
/// Pattern tokens (case-sensitive; unknown tokens stay verbatim):
///   Name/file:  {name} {ext} {origext} {parent} {parent2} {parent3} {drive} {depth}
///               {size} {sizeMB} {filesize-bytes}
///   Counters:   {counter} {counter2} (independent start/step/padding; style via CounterStyle)
///   Date/time (honors DateSource): {yyyy} {yy} {MM} {MMM} {MMMM} {dd} {ddd} {HH} {hh12}
///               {ampm} {mm} {ss} {date} {time} {week} {quarter} {dayofyear} {weekday}
///               {epoch} {age-days}
///   Camera/EXIF: {camera} {make} {model} {lens} {artist} {software} {width} {height} {mp}
///               {orientation} {fnumber} {iso-speed} {exposure} {focal}
///   Video:      {duration} (m.ss) {duration-s} (whole seconds)
///   GPS:        {lat} {lon} {gps}
///   Identity:   {hash8} {md5-8} {sha1-8} {crc32} {guid} {rand4} {rand8}
/// </summary>
public sealed class RenameOptions
{
    // ----- Pattern & dates -----
    public string Pattern { get; set; } = "{name}";
    public DateSource DateSource { get; set; } = DateSource.ExifThenFileDate;

    // ----- Numbering -----
    public int CounterStart { get; set; } = 1;
    public int CounterStep { get; set; } = 1;
    public int CounterPadding { get; set; } = 3;
    /// <summary>Restart the counter in each subfolder.</summary>
    public bool CounterPerFolder { get; set; }
    public CounterStyle CounterStyle { get; set; } = CounterStyle.Numeric;
    /// <summary>Which order files receive counter values.</summary>
    public NumberingOrder NumberingOrder { get; set; } = NumberingOrder.AsListed;
    /// <summary>Independent secondary counter for {counter2}.</summary>
    public int Counter2Start { get; set; } = 1;
    public int Counter2Step { get; set; } = 1;
    public int Counter2Padding { get; set; } = 2;

    // ----- Swap -----
    /// <summary>Swap the two halves of the name around the first occurrence of this separator ("A - B" → "B - A").</summary>
    public string SwapSeparator { get; set; } = " - ";
    public bool SwapEnabled { get; set; }

    // ----- Find & replace -----
    public List<FindReplaceRule> Replacements { get; set; } = [];

    // ----- Removes -----
    public RemoveRangeRule RemoveRange { get; set; } = new();
    public RemoveRangeRule RemoveRange2 { get; set; } = new();
    public RemoveBetweenRule RemoveBetween { get; set; } = new();
    public bool RemoveNumbers { get; set; }
    public bool RemoveLeadingNumbers { get; set; }
    public bool RemoveTrailingNumbers { get; set; }
    public bool RemoveBracketedText { get; set; }
    public bool RemovePunctuation { get; set; }
    public bool RemoveNonAscii { get; set; }
    public bool RemoveEmoji { get; set; }
    /// <summary>Strip common camera prefixes: IMG_ IMG- DSC_ DSCN DSCF DCIM PXL_ VID_ MVI_ GOPR "Screenshot_" "Screen Shot ".</summary>
    public bool RemoveCameraPrefixes { get; set; }
    /// <summary>Strip embedded date-like runs (20240601, 2024-06-01, 01.06.2024, ...).</summary>
    public bool RemoveDatePatterns { get; set; }
    /// <summary>Strip GUID-like runs (8-4-4-4-12 hex).</summary>
    public bool RemoveGuidPatterns { get; set; }
    /// <summary>Strip http(s)://... and www.... runs.</summary>
    public bool RemoveUrls { get; set; }
    /// <summary>Comma/space-separated words to remove (whole-word, case-insensitive), e.g. "copy final edited".</summary>
    public string RemoveWords { get; set; } = "";
    /// <summary>Characters to strip outright, e.g. "#&amp;!".</summary>
    public string StripCharacters { get; set; } = "";

    // ----- Inserts -----
    public InsertRule Insert { get; set; } = new();
    public InsertRule Insert2 { get; set; } = new();

    // ----- Hygiene -----
    public bool TrimWhitespace { get; set; } = true;
    public bool CollapseSpaces { get; set; }
    public bool RemoveDiacritics { get; set; }
    /// <summary>Best-effort full transliteration to ASCII (superset of RemoveDiacritics).</summary>
    public bool TransliterateToAscii { get; set; }
    public string? ReplaceSpacesWith { get; set; }
    public bool ReplaceUnderscoresWithSpaces { get; set; }
    /// <summary>Dots inside the name (never the extension dot) become spaces.</summary>
    public bool ReplaceDotsWithSpaces { get; set; }
    /// <summary>Collapse runs of - _ . and spaces mixed together into a single occurrence.</summary>
    public bool CollapseRepeatedSeparators { get; set; }
    /// <summary>Trim leading/trailing separator characters (space - _ .) from the name.</summary>
    public bool TrimSeparators { get; set; }
    /// <summary>Zero-pad every digit run in the name to this width (0 = off). "img2" → "img002" at width 3.</summary>
    public int PadNumberRunsTo { get; set; }
    /// <summary>Hard cap on name length before the extension (0 = off).</summary>
    public int MaxNameLength { get; set; }
    public TruncateFrom TruncateFrom { get; set; } = TruncateFrom.End;

    // ----- Case -----
    public CaseTransform NameCase { get; set; } = CaseTransform.None;
    public CaseTransform ExtensionCase { get; set; } = CaseTransform.None;
    /// <summary>TitleCase keeps these words lowercase when not first/last ("a an the of ...").</summary>
    public bool SmartTitleCase { get; set; } = true;
    public string SmallWords { get; set; } = "a an and as at but by for in nor of on or so the to up yet";
    /// <summary>Words whose typed casing survives all case transforms, e.g. "USA iPhone HDR 4K".</summary>
    public string PreserveCaseWords { get; set; } = "";

    // ----- Affixes -----
    public string Prefix { get; set; } = "";
    /// <summary>Skip the prefix when the name already starts with it.</summary>
    public bool PrefixOnlyIfMissing { get; set; } = true;
    public string Suffix { get; set; } = "";
    public bool SuffixOnlyIfMissing { get; set; } = true;
    /// <summary>Prepend the immediate parent folder name.</summary>
    public bool ParentFolderAsPrefix { get; set; }
    public string ParentPrefixSeparator { get; set; } = " - ";

    // ----- Extension operations -----
    /// <summary>Replace the extension outright (without dot, e.g. "jpg"); empty = keep.</summary>
    public string NewExtension { get; set; } = "";
    public bool RemoveExtension { get; set; }
    /// <summary>Normalize synonyms: jpeg→jpg, tiff→tif, mpeg→mpg, htm→html.</summary>
    public bool NormalizeExtensions { get; set; }
    /// <summary>Sniff magic bytes (JPEG/PNG/GIF/BMP/TIFF/HEIC/MP4/MOV/AVI/MKV-WebM) and correct a lying extension.</summary>
    public bool FixExtensionBySniffing { get; set; }

    // ----- Scope & filters -----
    /// <summary>Wildcard mask like "IMG_*.jpg" (regex when UseRegexMasks); empty = all selected files.</summary>
    public string IncludeMask { get; set; } = "";
    public string ExcludeMask { get; set; } = "";
    public bool UseRegexMasks { get; set; }
    public bool IncludeSubfolders { get; set; }
    public long MinSizeBytes { get; set; }
    /// <summary>0 = no upper bound.</summary>
    public long MaxSizeBytes { get; set; }
    public DateTime? ModifiedAfter { get; set; }
    public DateTime? ModifiedBefore { get; set; }
    /// <summary>Only files whose metadata yields an EXIF/container date.</summary>
    public bool OnlyWithExif { get; set; }
    public bool SkipHiddenSystem { get; set; } = true;

    // ----- Safety -----
    public RenameConflictPolicy ConflictPolicy { get; set; } = RenameConflictPolicy.AppendNumber;
    /// <summary>Collision suffix template; {n} is the counter. "_{n}" → "name_1", " ({n})" → "name (1)".</summary>
    public string CollisionSuffixFormat { get; set; } = "_{n}";
    /// <summary>Write an old-path→new-path CSV next to the first renamed file.</summary>
    public bool ExportMappingCsv { get; set; }

    /// <summary>Deep clone via JSON round-trip — immune to future field additions.</summary>
    public RenameOptions Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<RenameOptions>(
            System.Text.Json.JsonSerializer.Serialize(this))!;
}

/// <summary>One row of the rename preview grid.</summary>
public sealed class RenamePlanItem
{
    public required string OldPath { get; init; }
    public required string NewName { get; set; }
    public string NewPath => Path.Combine(Path.GetDirectoryName(OldPath) ?? "", NewName);
    public bool Changed => !string.Equals(Path.GetFileName(OldPath), NewName, StringComparison.Ordinal);
    /// <summary>Non-null when this rename cannot proceed (collision under Fail policy, invalid name, ...).</summary>
    public string? Problem { get; set; }
}

/// <summary>Outcome of an executed batch rename.</summary>
public sealed class RenameResult
{
    public int Renamed { get; set; }
    public int Skipped { get; set; }
    public List<(string File, string Error)> Errors { get; } = [];
    public string? JournalPath { get; set; }
    /// <summary>Path of the old→new mapping CSV when ExportMappingCsv was on.</summary>
    public string? MappingCsvPath { get; set; }
}
