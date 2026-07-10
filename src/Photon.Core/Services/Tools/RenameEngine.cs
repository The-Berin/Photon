using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>
/// Batch rename engine (feature 1): pure planning plus journaled execution.
/// Pipeline order matches the <see cref="RenameOptions"/> doc: pattern → swap →
/// find/replace → removes → inserts → hygiene → case → affixes → extension ops →
/// sanitize → collision handling.
/// </summary>
public sealed class RenameEngine : IRenameEngine
{
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
        string? maskProblem = null;
        var include = CompileMaskChecked(options.IncludeMask, options.UseRegexMasks, ref maskProblem);
        var exclude = CompileMaskChecked(options.ExcludeMask, options.UseRegexMasks, ref maskProblem);

        var selected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files)
        {
            if (!seen.Add(path)) continue;
            var name = Path.GetFileName(path);
            if (include is not null && !include.IsMatch(name)) continue;
            if (exclude is not null && exclude.IsMatch(name)) continue;
            if (!PassesFileFilters(path, options)) continue;
            selected.Add(path);
        }

        var counters = AssignCounters(selected, options);

        var plan = new List<RenamePlanItem>();
        // New full paths claimed by earlier rows, so in-plan collisions are caught
        // (case-insensitively, matching Windows) before any disk change happens.
        var plannedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in selected)
        {
            var originalName = Path.GetFileName(path);
            var dir = Path.GetDirectoryName(path) ?? "";
            var (counter, counter2) = counters.TryGetValue(path, out var c) ? c : (0, 0);

            var (newName, problem) = BuildNewName(path, counter, counter2, options);

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
                            newPath = MakeUniqueWithTemplate(newPath, options.CollisionSuffixFormat, TargetTaken);
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

            // An unparseable regex mask blocks the whole batch and is flagged on every row
            // so the UI cannot miss it (the masks themselves were ignored above).
            if (maskProblem is not null)
                problem = problem is null ? maskProblem : $"{maskProblem}; {problem}";

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
                    // The disk may have changed since planning; re-check and resolve per policy.
                    bool TakenOnDisk(string candidate) =>
                        !string.Equals(candidate, row.OldPath, StringComparison.OrdinalIgnoreCase)
                        && (File.Exists(candidate) || Directory.Exists(candidate));

                    var targetPath = row.NewPath;
                    if (TakenOnDisk(targetPath))
                    {
                        if (options.ConflictPolicy == RenameConflictPolicy.AppendNumber)
                        {
                            targetPath = MakeUniqueWithTemplate(targetPath, options.CollisionSuffixFormat, TakenOnDisk);
                        }
                        else
                        {
                            if (options.ConflictPolicy == RenameConflictPolicy.Fail)
                                result.Errors.Add((row.OldPath, "target exists"));
                            else
                                result.Skipped++;
                            continue;
                        }
                    }

                    long size = 0;
                    try { size = new FileInfo(row.OldPath).Length; } catch { /* size is best-effort */ }
                    MoveHonoringCase(row.OldPath, targetPath);
                    journal.Entries.Add(new JournalEntry
                    {
                        Operation = JournalOperation.RenamedInPlace,
                        OriginalPath = row.OldPath,
                        NewPath = targetPath,
                        SizeBytes = size,
                    });
                    result.Renamed++;
                    bytes += size;
                }
                catch (Exception ex)
                {
                    result.Errors.Add((row.OldPath, ex.Message));
                }
                finally
                {
                    done++;
                    progress?.Report(ToolsCommon.MakeProgress(row.OldPath, done, rows.Count, bytes, 0, stopwatch));
                    // A crash mid-run must not lose the record of renames already applied.
                    checkpoint.MaybeSave();
                }
            }
        }, CancellationToken.None);

        if (journal.Entries.Count > 0)
        {
            await _journal.SaveAsync(journal, CancellationToken.None);
            result.JournalPath = ToolsCommon.JournalFilePath(_journal, journal);
            if (options.ExportMappingCsv) WriteMappingCsv(journal, result);
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

    // ---------- scope filters ----------

    /// <summary>Wildcard masks compile as before; regex masks that do not parse null out and set the problem.</summary>
    private static Regex? CompileMaskChecked(string mask, bool useRegex, ref string? problem)
    {
        if (string.IsNullOrWhiteSpace(mask)) return null;
        if (!useRegex) return ToolsCommon.CompileMask(mask);
        try
        {
            return new Regex(mask, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            var message = $"[invalid mask] {mask}";
            problem = problem is null ? message : $"{problem}; {message}";
            return null;
        }
    }

    private bool PassesFileFilters(string path, RenameOptions o)
    {
        if (o.SkipHiddenSystem)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0) return false;
            }
            catch { /* attributes unreadable: keep the file in scope */ }
        }
        if (o.MinSizeBytes > 0 || o.MaxSizeBytes > 0 || o.ModifiedAfter is not null || o.ModifiedBefore is not null)
        {
            try
            {
                var info = new FileInfo(path);
                if (o.MinSizeBytes > 0 && info.Length < o.MinSizeBytes) return false;
                if (o.MaxSizeBytes > 0 && info.Length > o.MaxSizeBytes) return false;
                if (o.ModifiedAfter is DateTime after && info.LastWriteTime < after) return false;
                if (o.ModifiedBefore is DateTime before && info.LastWriteTime > before) return false;
            }
            catch { /* unreadable file info: keep the file in scope */ }
        }
        if (o.OnlyWithExif && !HasMetadataDate(path)) return false;
        return true;
    }

    private bool HasMetadataDate(string path)
    {
        try
        {
            var media = new MediaFile
            {
                FilePath = path,
                IsVideo = ScanFilter.DefaultVideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()),
            };
            _metadata.Populate(media);
            return media.ExifDate is not null;
        }
        catch
        {
            return false;
        }
    }

    // ---------- numbering ----------

    /// <summary>
    /// Assigns each file its {counter} and {counter2} values in NumberingOrder. Only
    /// {counter} honors CounterPerFolder; {counter2} always counts across the whole batch.
    /// </summary>
    private static Dictionary<string, (int Counter, int Counter2)> AssignCounters(
        List<string> files, RenameOptions o)
    {
        var result = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        bool usesCounters = o.Pattern.Contains("{counter}", StringComparison.Ordinal)
                         || o.Pattern.Contains("{counter2}", StringComparison.Ordinal);
        if (!usesCounters) return result;

        int global = o.CounterStart;
        int counter2 = o.Counter2Start;
        var folderCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in OrderForNumbering(files, o.NumberingOrder))
        {
            int counter;
            if (o.CounterPerFolder)
            {
                var dir = Path.GetDirectoryName(path) ?? "";
                if (!folderCounters.TryGetValue(dir, out counter)) counter = o.CounterStart;
                folderCounters[dir] = counter + o.CounterStep;
            }
            else
            {
                counter = global;
                global += o.CounterStep;
            }
            result[path] = (counter, counter2);
            counter2 += o.Counter2Step;
        }
        return result;
    }

    private static IEnumerable<string> OrderForNumbering(List<string> files, NumberingOrder order) => order switch
    {
        NumberingOrder.NameAscending => files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase),
        NumberingOrder.NameDescending => files.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase),
        NumberingOrder.DateAscending => files.OrderBy(BestDate),
        NumberingOrder.DateDescending => files.OrderByDescending(BestDate),
        NumberingOrder.SizeAscending => files.OrderBy(SafeLength),
        NumberingOrder.SizeDescending => files.OrderByDescending(SafeLength),
        NumberingOrder.PathAscending => files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
        _ => files, // AsListed
    };

    /// <summary>min(created, modified) — same heuristic as DateResolver; unreadable files sort last.</summary>
    private static DateTime BestDate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.CreationTime <= info.LastWriteTime ? info.CreationTime : info.LastWriteTime;
        }
        catch
        {
            return DateTime.MaxValue;
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    // ---------- name pipeline ----------

    private (string NewName, string? Problem) BuildNewName(string path, int counter, int counter2,
        RenameOptions options)
    {
        var originalName = Path.GetFileName(path);

        var ctx = new RenameTokenContext(path, counter, counter2, options, _metadata, _dates);
        var stem = RenameTokens.Expand(options.Pattern, ctx);
        if (ctx.Problem is not null) return (originalName, ctx.Problem);

        var originalExt = Path.GetExtension(path);
        var ext = originalExt.Length > 1 ? originalExt[1..] : "";

        if (options.SwapEnabled) stem = RenameTextOps.SwapAroundSeparator(stem, options.SwapSeparator);

        string? problem = null;
        (stem, ext) = ApplyReplacements(stem, ext, options.Replacements, ref problem);
        if (problem is not null) return (originalName, problem);

        stem = ApplyRemoves(stem, options);
        stem = RenameTextOps.ApplyInsert(stem, options.Insert);
        stem = RenameTextOps.ApplyInsert(stem, options.Insert2);
        stem = ApplyHygiene(stem, options);
        stem = RenameTextOps.ApplyNameCase(stem, options);
        stem = ApplyAffixes(stem, path, options);
        ext = ApplyExtensionOps(ext, path, options);
        ext = RenameTextOps.ApplyCase(ext, options.ExtensionCase);

        stem = PathSanitizer.SanitizeSegment(stem);
        ext = RenameTextOps.SanitizeExtension(ext);
        return (ext.Length > 0 ? stem + "." + ext : stem, null);
    }

    private static string ApplyRemoves(string stem, RenameOptions o)
    {
        stem = RenameTextOps.ApplyRemoveRange(stem, o.RemoveRange);
        stem = RenameTextOps.ApplyRemoveRange(stem, o.RemoveRange2);
        stem = RenameTextOps.ApplyRemoveBetween(stem, o.RemoveBetween);
        if (o.RemoveCameraPrefixes) stem = RenameTextOps.RemoveCameraPrefixes(stem);
        if (o.RemoveDatePatterns) stem = RenameTextOps.RemoveDatePatterns(stem);
        if (o.RemoveGuidPatterns) stem = RenameTextOps.RemoveGuidPatterns(stem);
        if (o.RemoveUrls) stem = RenameTextOps.RemoveUrls(stem);
        if (o.RemoveWords.Length > 0) stem = RenameTextOps.RemoveWords(stem, o.RemoveWords);
        if (o.RemoveBracketedText) stem = RenameTextOps.RemoveBracketed(stem);
        if (o.RemoveNumbers)
        {
            stem = new string([.. stem.Where(c => !char.IsDigit(c))]);
        }
        else
        {
            if (o.RemoveLeadingNumbers) stem = RenameTextOps.RemoveLeadingDigits(stem);
            if (o.RemoveTrailingNumbers) stem = RenameTextOps.RemoveTrailingDigits(stem);
        }
        if (o.RemovePunctuation) stem = RenameTextOps.RemovePunctuation(stem);
        if (o.RemoveNonAscii) stem = RenameTextOps.RemoveNonAscii(stem);
        if (o.RemoveEmoji) stem = RenameTextOps.RemoveEmoji(stem);
        foreach (var c in o.StripCharacters)
            stem = stem.Replace(c.ToString(), "", StringComparison.Ordinal);
        return stem;
    }

    private static string ApplyHygiene(string stem, RenameOptions o)
    {
        if (o.PadNumberRunsTo > 0) stem = RenameTextOps.PadNumberRuns(stem, o.PadNumberRunsTo);
        if (o.ReplaceUnderscoresWithSpaces) stem = stem.Replace('_', ' ');
        if (o.ReplaceDotsWithSpaces) stem = stem.Replace('.', ' '); // stem only — never the extension dot
        if (o.CollapseRepeatedSeparators) stem = RenameTextOps.CollapseRepeatedSeparators(stem);
        if (o.ReplaceSpacesWith is not null)
            stem = stem.Replace(" ", o.ReplaceSpacesWith, StringComparison.Ordinal);
        if (o.CollapseSpaces) stem = MultiSpace.Replace(stem, " ");
        if (o.RemoveDiacritics) stem = RenameTextOps.RemoveDiacritics(stem);
        if (o.TransliterateToAscii) stem = RenameTextOps.TransliterateToAscii(stem);
        if (o.TrimSeparators) stem = RenameTextOps.TrimSeparators(stem);
        if (o.TrimWhitespace) stem = stem.Trim();
        if (o.MaxNameLength > 0) stem = RenameTextOps.Truncate(stem, o.MaxNameLength, o.TruncateFrom);
        return stem;
    }

    /// <summary>Prefix, then parent folder in front of it (parent + prefix + name), then suffix.</summary>
    private static string ApplyAffixes(string stem, string path, RenameOptions o)
    {
        if (o.Prefix.Length > 0
            && !(o.PrefixOnlyIfMissing && stem.StartsWith(o.Prefix, StringComparison.OrdinalIgnoreCase)))
            stem = o.Prefix + stem;
        if (o.Suffix.Length > 0
            && !(o.SuffixOnlyIfMissing && stem.EndsWith(o.Suffix, StringComparison.OrdinalIgnoreCase)))
            stem += o.Suffix;
        if (o.ParentFolderAsPrefix)
        {
            var dir = Path.GetDirectoryName(path);
            var parent = dir is null ? "" : Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
            if (parent.Length > 0)
                stem = PathSanitizer.SanitizeSegment(parent) + o.ParentPrefixSeparator + stem;
        }
        return stem;
    }

    /// <summary>Precedence: RemoveExtension &gt; NewExtension &gt; FixExtensionBySniffing &gt; NormalizeExtensions.</summary>
    private static string ApplyExtensionOps(string ext, string path, RenameOptions o)
    {
        if (o.RemoveExtension) return "";
        var replacement = o.NewExtension.Trim();
        if (replacement.Length > 0) return replacement.TrimStart('.');

        bool corrected = false;
        if (o.FixExtensionBySniffing)
        {
            var sniffed = ExtensionSniffer.Sniff(path);
            if (sniffed is not null && !ExtensionSniffer.MatchesSniffed(sniffed, ext))
            {
                ext = sniffed;
                corrected = true;
            }
        }
        if (!corrected && o.NormalizeExtensions) ext = ExtensionSniffer.Normalize(ext);
        return ext;
    }

    // ---------- find & replace ----------

    private static (string Stem, string Ext) ApplyReplacements(string stem, string ext,
        List<FindReplaceRule> rules, ref string? problem)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.Find.Length == 0) continue;
            if (rule.Target != ReplaceTarget.ExtensionOnly)
            {
                stem = ApplyOneReplacement(stem, rule, ref problem);
                if (problem is not null) return (stem, ext);
            }
            if (rule.Target != ReplaceTarget.NameOnly)
            {
                ext = ApplyOneReplacement(ext, rule, ref problem);
                if (problem is not null) return (stem, ext);
            }
        }
        return (stem, ext);
    }

    private static string ApplyOneReplacement(string text, FindReplaceRule rule, ref string? problem)
    {
        if (rule.UseRegex || rule.WholeWord)
        {
            // WholeWord is a literal find wrapped in word boundaries (regex rules ignore the flag);
            // its replacement text must not be treated as a substitution pattern.
            var pattern = rule.UseRegex ? rule.Find : @"\b" + Regex.Escape(rule.Find) + @"\b";
            var replacement = rule.UseRegex ? rule.Replace : rule.Replace.Replace("$", "$$");
            try
            {
                var flags = RegexOptions.CultureInvariant
                    | (rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                var regex = new Regex(pattern, flags, RegexTimeout);
                return rule.FirstOnly ? regex.Replace(text, replacement, 1) : regex.Replace(text, replacement);
            }
            catch (ArgumentException)
            {
                problem = $"invalid regex: {rule.Find}";
            }
            catch (RegexMatchTimeoutException)
            {
                problem = $"regex timed out: {rule.Find}";
            }
            return text;
        }
        var comparison = rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (rule.FirstOnly)
        {
            int idx = text.IndexOf(rule.Find, comparison);
            return idx < 0 ? text : text[..idx] + rule.Replace + text[(idx + rule.Find.Length)..];
        }
        return text.Replace(rule.Find, rule.Replace, comparison);
    }

    // ---------- collisions & mapping ----------

    /// <summary>
    /// Appends the collision suffix template ({n} = attempt number) before the extension until
    /// the name is free. A template without {n} gets the number appended after it.
    /// </summary>
    private static string MakeUniqueWithTemplate(string fullPath, string template, Func<string, bool> taken)
    {
        if (!taken(fullPath)) return fullPath;
        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);
        var suffix = string.IsNullOrEmpty(template) ? "_{n}" : template;
        if (!suffix.Contains("{n}", StringComparison.Ordinal)) suffix += "{n}";
        for (int i = 1; ; i++)
        {
            var rendered = suffix.Replace("{n}", i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var candidate = Path.Combine(dir, stem + rendered + ext);
            if (!taken(candidate)) return candidate;
        }
    }

    /// <summary>Writes the old→new mapping CSV next to the first renamed file (best-effort).</summary>
    private static void WriteMappingCsv(SortJournal journal, RenameResult result)
    {
        var csvPath = "";
        try
        {
            var dir = Path.GetDirectoryName(journal.Entries[0].NewPath) ?? journal.SourceFolder;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            csvPath = Path.Combine(dir, $"photon-rename-map-{stamp}.csv");
            var lines = new List<string>(journal.Entries.Count + 1) { "old_path,new_path" };
            foreach (var entry in journal.Entries)
                lines.Add(CsvField(entry.OriginalPath) + "," + CsvField(entry.NewPath ?? ""));
            File.WriteAllLines(csvPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            result.MappingCsvPath = csvPath;
        }
        catch (Exception ex)
        {
            result.Errors.Add((csvPath.Length > 0 ? csvPath : "mapping csv", "Could not write mapping CSV: " + ex.Message));
        }
    }

    private static string CsvField(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
