namespace Photon.Core.Models;

/// <summary>One find/replace step in the rename pipeline.</summary>
public sealed class FindReplaceRule
{
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
    public bool UseRegex { get; set; }
    public bool CaseSensitive { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>Insert fixed text at a position in the name.</summary>
public sealed class InsertRule
{
    public string Text { get; set; } = "";
    public int Position { get; set; }
    public bool FromEnd { get; set; }
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

/// <summary>
/// The batch renamer's full option set — the "NASA control center" panel binds to this.
/// Pipeline order: pattern → find/replace steps → remove → insert → strip/trim →
/// case transforms → prefix/suffix → collision handling.
///
/// Pattern tokens (case-sensitive): {name} {ext} {counter} {yyyy} {yy} {MM} {MMM} {MMMM}
/// {dd} {ddd} {HH} {mm} {ss} {date} {time} {camera} {make} {model} {width} {height}
/// {mp} {size} {sizeMB} {parent} {parent2} {hash8} {guid} {rand4} {rand8}
/// Dates honor <see cref="DateSource"/>. Unknown tokens are left verbatim.
/// </summary>
public sealed class RenameOptions
{
    // Pattern
    public string Pattern { get; set; } = "{name}";
    public DateSource DateSource { get; set; } = DateSource.ExifThenFileDate;

    // Counter
    public int CounterStart { get; set; } = 1;
    public int CounterStep { get; set; } = 1;
    public int CounterPadding { get; set; } = 3;
    /// <summary>Restart the counter in each subfolder.</summary>
    public bool CounterPerFolder { get; set; }

    // Pipeline steps
    public List<FindReplaceRule> Replacements { get; set; } = [];
    public RemoveRangeRule RemoveRange { get; set; } = new();
    public bool RemoveNumbers { get; set; }
    public bool RemoveBracketedText { get; set; }
    public InsertRule Insert { get; set; } = new();
    public bool TrimWhitespace { get; set; } = true;
    public bool CollapseSpaces { get; set; }
    public bool RemoveDiacritics { get; set; }
    /// <summary>Characters to strip outright, e.g. "#&amp;!".</summary>
    public string StripCharacters { get; set; } = "";
    public string? ReplaceSpacesWith { get; set; }
    public CaseTransform NameCase { get; set; } = CaseTransform.None;
    public CaseTransform ExtensionCase { get; set; } = CaseTransform.None;
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";

    // Scope
    /// <summary>Wildcard mask like "IMG_*.jpg"; empty = all selected files.</summary>
    public string IncludeMask { get; set; } = "";
    public string ExcludeMask { get; set; } = "";
    public bool IncludeSubfolders { get; set; }

    // Safety
    public RenameConflictPolicy ConflictPolicy { get; set; } = RenameConflictPolicy.AppendNumber;

    public RenameOptions Clone()
    {
        var c = (RenameOptions)MemberwiseClone();
        c.Replacements = Replacements.Select(r => new FindReplaceRule
        {
            Find = r.Find, Replace = r.Replace, UseRegex = r.UseRegex,
            CaseSensitive = r.CaseSensitive, Enabled = r.Enabled,
        }).ToList();
        c.RemoveRange = new RemoveRangeRule { Start = RemoveRange.Start, Count = RemoveRange.Count, FromEnd = RemoveRange.FromEnd, Enabled = RemoveRange.Enabled };
        c.Insert = new InsertRule { Text = Insert.Text, Position = Insert.Position, FromEnd = Insert.FromEnd, Enabled = Insert.Enabled };
        return c;
    }
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
}
