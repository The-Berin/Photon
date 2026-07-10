using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>
/// Batch rename engine (feature 1): pure planning plus journaled execution.
/// Pipeline order matches the <see cref="RenameOptions"/> doc: pattern → find/replace →
/// remove → insert → strip/trim → case → prefix/suffix → collision handling.
/// </summary>
public sealed class RenameEngine : IRenameEngine
{
    private static readonly Regex BracketedText = new(@"\([^()]*\)|\[[^\[\]]*\]|\{[^{}]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MultiSpace = new(@" {2,}", RegexOptions.Compiled);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly IJournalService _journal;
    private readonly IMetadataReader _metadata;
    private readonly IDateResolver _dates;

    public RenameEngine() : this(new JournalService(), new MetadataReader(), new DateResolver()) { }

    public RenameEngine(IJournalService journal) : this(journal, new MetadataReader(), new DateResolver()) { }

    public RenameEngine(IJournalService journal, IMetadataReader metadataReader, IDateResolver dateResolver)
    {
        _journal = journal;
        _metadata = metadataReader;
        _dates = dateResolver;
    }

    public List<RenamePlanItem> BuildPlan(IReadOnlyList<string> files, RenameOptions options)
    {
        var plan = new List<RenamePlanItem>();
        var include = ToolsCommon.CompileMask(options.IncludeMask);
        var exclude = ToolsCommon.CompileMask(options.ExcludeMask);
        bool usesCounter = options.Pattern.Contains("{counter}", StringComparison.Ordinal);
        int globalCounter = options.CounterStart;
        var folderCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // New full paths claimed by earlier rows, so in-plan collisions are caught
        // (case-insensitively, matching Windows) before any disk change happens.
        var plannedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in files)
        {
            if (!seen.Add(path)) continue;
            var originalName = Path.GetFileName(path);
            if (include is not null && !include.IsMatch(originalName)) continue;
            if (exclude is not null && exclude.IsMatch(originalName)) continue;

            var dir = Path.GetDirectoryName(path) ?? "";
            int counter = 0;
            if (usesCounter)
            {
                if (options.CounterPerFolder)
                {
                    if (!folderCounters.TryGetValue(dir, out counter)) counter = options.CounterStart;
                    folderCounters[dir] = counter + options.CounterStep;
                }
                else
                {
                    counter = globalCounter;
                    globalCounter += options.CounterStep;
                }
            }

            var (newName, problem) = BuildNewName(path, counter, options);

            if (problem is null && !string.Equals(newName, originalName, StringComparison.Ordinal))
            {
                var newPath = Path.Combine(dir, newName);

                // A case-only rename of the file onto itself is legal, so the disk check
                // ignores the source path (compared case-insensitively, as Windows would).
                bool TargetTaken(string candidate) =>
                    plannedTargets.Contains(candidate)
                    || (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase)
                        && (File.Exists(candidate) || Directory.Exists(candidate)));

                if (TargetTaken(newPath))
                {
                    switch (options.ConflictPolicy)
                    {
                        case RenameConflictPolicy.AppendNumber:
                            newPath = PathSanitizer.MakeUnique(newPath, TargetTaken);
                            newName = Path.GetFileName(newPath);
                            break;
                        case RenameConflictPolicy.Skip:
                            problem = "target exists";
                            newName = originalName;
                            break;
                        case RenameConflictPolicy.Fail:
                            problem = "target exists";
                            break;
                    }
                }
            }

            var item = new RenamePlanItem { OldPath = path, NewName = newName, Problem = problem };
            if (item.Changed && item.Problem is null) plannedTargets.Add(item.NewPath);
            plan.Add(item);
        }
        return plan;
    }

    public async Task<RenameResult> ExecuteAsync(IReadOnlyList<RenamePlanItem> plan, RenameOptions options,
        IProgress<SortProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new RenameResult { Skipped = plan.Count(p => p.Problem is not null) };
        var rows = plan.Where(p => p.Changed && p.Problem is null).ToList();
        if (rows.Count == 0) return result;

        var sourceFolder = Path.GetDirectoryName(rows[0].OldPath) ?? "";
        var journal = new SortJournal
        {
            TimestampUtc = DateTime.UtcNow,
            OperationKind = "Batch rename",
            SourceFolder = sourceFolder,
            DestinationRoot = sourceFolder,
            Action = SortAction.Move,
        };

        var checkpoint = new JournalCheckpoint(_journal, journal);
        await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            int done = 0;
            long bytes = 0;
            foreach (var row in rows)
            {
                // Stop between files on cancel; the journal below still records what happened.
                if (ct.IsCancellationRequested) break;
                try
                {
                    long size = 0;
                    try { size = new FileInfo(row.OldPath).Length; } catch { /* size is best-effort */ }
                    MoveHonoringCase(row.OldPath, row.NewPath);
                    journal.Entries.Add(new JournalEntry
                    {
                        Operation = JournalOperation.RenamedInPlace,
                        OriginalPath = row.OldPath,
                        NewPath = row.NewPath,
                        SizeBytes = size,
                    });
                    result.Renamed++;
                    bytes += size;
                }
                catch (Exception ex)
                {
                    result.Errors.Add((row.OldPath, ex.Message));
                }
                done++;
                progress?.Report(ToolsCommon.MakeProgress(row.OldPath, done, rows.Count, bytes, 0, stopwatch));
                // A crash mid-run must not lose the record of renames already applied.
                checkpoint.MaybeSave();
            }
        }, CancellationToken.None);

        if (journal.Entries.Count > 0)
        {
            await _journal.SaveAsync(journal, CancellationToken.None);
            result.JournalPath = ToolsCommon.JournalFilePath(_journal, journal);
        }
        return result;
    }

    /// <summary>
    /// Case-only renames hop through a temporary name: Windows treats source and target as
    /// the same file, so a direct move cannot be relied on to change the stored casing.
    /// </summary>
    private static void MoveHonoringCase(string oldPath, string newPath)
    {
        bool caseOnly = string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(oldPath, newPath, StringComparison.Ordinal);
        if (!caseOnly)
        {
            File.Move(oldPath, newPath);
            return;
        }
        var temp = oldPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.Move(oldPath, temp);
        try { File.Move(temp, newPath); }
        catch { File.Move(temp, oldPath); throw; }
    }

    private (string NewName, string? Problem) BuildNewName(string path, int counter, RenameOptions options)
    {
        var originalName = Path.GetFileName(path);

        var ctx = new RenameTokenContext(path, counter, options, _metadata, _dates);
        var stem = RenameTokens.Expand(options.Pattern, ctx);
        if (ctx.Problem is not null) return (originalName, ctx.Problem);

        string? problem = null;
        stem = ApplyReplacements(stem, options.Replacements, ref problem);
        if (problem is not null) return (originalName, problem);

        stem = ApplyRemoveRange(stem, options.RemoveRange);
        if (options.RemoveNumbers) stem = new string([.. stem.Where(c => !char.IsDigit(c))]);
        if (options.RemoveBracketedText) stem = RemoveBracketed(stem);
        stem = ApplyInsert(stem, options.Insert);

        foreach (var c in options.StripCharacters)
            stem = stem.Replace(c.ToString(), "", StringComparison.Ordinal);
        if (options.TrimWhitespace) stem = stem.Trim();
        if (options.CollapseSpaces) stem = MultiSpace.Replace(stem, " ");
        if (options.RemoveDiacritics) stem = RemoveDiacritics(stem);
        if (options.ReplaceSpacesWith is not null)
            stem = stem.Replace(" ", options.ReplaceSpacesWith, StringComparison.Ordinal);

        stem = ApplyCase(stem, options.NameCase);
        stem = options.Prefix + stem + options.Suffix;
        stem = PathSanitizer.SanitizeSegment(stem);

        var ext = Path.GetExtension(path);
        var extText = ext.Length > 1 ? ApplyCase(ext[1..], options.ExtensionCase) : "";
        return (extText.Length > 0 ? stem + "." + extText : stem, null);
    }

    private static string ApplyReplacements(string stem, List<FindReplaceRule> rules, ref string? problem)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.Find.Length == 0) continue;
            if (rule.UseRegex)
            {
                try
                {
                    var flags = RegexOptions.CultureInvariant
                        | (rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                    stem = Regex.Replace(stem, rule.Find, rule.Replace, flags, RegexTimeout);
                }
                catch (ArgumentException)
                {
                    problem = $"invalid regex: {rule.Find}";
                    return stem;
                }
                catch (RegexMatchTimeoutException)
                {
                    problem = $"regex timed out: {rule.Find}";
                    return stem;
                }
            }
            else
            {
                stem = stem.Replace(rule.Find, rule.Replace,
                    rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            }
        }
        return stem;
    }

    /// <summary>
    /// Start is a 0-based offset from the front; with FromEnd the removed range instead
    /// ends Start characters before the end of the name. Out-of-range values are clamped.
    /// </summary>
    private static string ApplyRemoveRange(string stem, RemoveRangeRule rule)
    {
        if (!rule.Enabled || rule.Count <= 0 || stem.Length == 0) return stem;
        int start = rule.FromEnd ? stem.Length - rule.Start - rule.Count : rule.Start;
        int count = rule.Count;
        if (start < 0) { count += start; start = 0; }
        if (start >= stem.Length || count <= 0) return stem;
        count = Math.Min(count, stem.Length - start);
        return stem.Remove(start, count);
    }

    private static string RemoveBracketed(string stem)
    {
        // Innermost-first so nested brackets collapse fully.
        string previous;
        do
        {
            previous = stem;
            stem = BracketedText.Replace(stem, "");
        } while (!string.Equals(stem, previous, StringComparison.Ordinal));
        return stem;
    }

    private static string ApplyInsert(string stem, InsertRule rule)
    {
        if (!rule.Enabled || rule.Text.Length == 0) return stem;
        int position = rule.FromEnd ? stem.Length - rule.Position : rule.Position;
        return stem.Insert(Math.Clamp(position, 0, stem.Length), rule.Text);
    }

    private static string ApplyCase(string s, CaseTransform transform) => transform switch
    {
        CaseTransform.Lower => s.ToLowerInvariant(),
        CaseTransform.Upper => s.ToUpperInvariant(),
        // ToTitleCase leaves ALL-CAPS words alone, so lower-case first.
        CaseTransform.TitleCase => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()),
        CaseTransform.SentenceCase => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant(),
        CaseTransform.InvertCase => string.Create(s.Length, s, static (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
                span[i] = char.IsUpper(src[i]) ? char.ToLowerInvariant(src[i])
                        : char.IsLower(src[i]) ? char.ToUpperInvariant(src[i])
                        : src[i];
        }),
        _ => s,
    };

    private static string RemoveDiacritics(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
