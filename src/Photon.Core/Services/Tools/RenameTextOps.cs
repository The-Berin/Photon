using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Photon.Core.Models;

namespace Photon.Core.Services;

/// <summary>
/// Pure string operations for the batch renamer: swaps, removes, inserts, hygiene and
/// case transforms. Everything here is deterministic and touches no file system state.
/// </summary>
internal static class RenameTextOps
{
    private static readonly RegexOptions Rx = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex BracketedText = new(@"\([^()]*\)|\[[^\[\]]*\]|\{[^{}]*\}", Rx);
    private static readonly Regex DigitRuns = new(@"\d+", Rx);
    // Backreference forces the same separator on both sides, so "2024-06_01" is left alone.
    private static readonly Regex DateYmd = new(@"(?<!\d)(19|20)\d{2}([-_.]?)(0[1-9]|1[0-2])\2(0[1-9]|[12]\d|3[01])(?!\d)", Rx);
    private static readonly Regex DateDmy = new(@"(?<!\d)(0[1-9]|[12]\d|3[01])([-_.])(0[1-9]|1[0-2])\2(19|20)\d{2}(?!\d)", Rx);
    private static readonly Regex GuidRun = new(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", Rx | RegexOptions.IgnoreCase);
    private static readonly Regex UrlRun = new(@"https?://\S+|www\.\S+", Rx | RegexOptions.IgnoreCase);
    // A run of two or more separator chars (mixed or repeated) collapses to the run's FIRST char.
    private static readonly Regex SeparatorRun = new(@"([-_. ])[-_. ]+", Rx);
    // "Word" for case transforms and preserve-case matching: a separator-delimited token.
    private static readonly Regex WordTokens = new(@"[^ \-_.]+", Rx);

    private static readonly char[] SeparatorChars = [' ', '-', '_', '.'];
    private static readonly char[] InvalidNameChars = ['"', '<', '>', '|', ':', '*', '?', '\\', '/'];

    // Longest-first so overlapping prefixes strip greedily; applied repeatedly from the start.
    private static readonly string[] CameraPrefixes =
        ["Screen Shot ", "Screenshot_", "DSCN", "DSCF", "DCIM", "GOPR", "IMG_", "IMG-", "PXL_", "VID_", "MVI_", "DSC_"];

    // ---------- swap ----------

    /// <summary>"A&lt;sep&gt;B" → "B&lt;sep&gt;A" around the FIRST occurrence of the separator.</summary>
    internal static string SwapAroundSeparator(string s, string separator)
    {
        if (separator.Length == 0) return s;
        int i = s.IndexOf(separator, StringComparison.Ordinal);
        if (i < 0) return s;
        return s[(i + separator.Length)..] + separator + s[..i];
    }

    // ---------- removes ----------

    /// <summary>
    /// Start is a 0-based offset from the front; with FromEnd the removed range instead
    /// ends Start characters before the end of the name. Out-of-range values are clamped.
    /// </summary>
    internal static string ApplyRemoveRange(string stem, RemoveRangeRule rule)
    {
        if (!rule.Enabled || rule.Count <= 0 || stem.Length == 0) return stem;
        int start = rule.FromEnd ? stem.Length - rule.Start - rule.Count : rule.Start;
        int count = rule.Count;
        if (start < 0) { count += start; start = 0; }
        if (start >= stem.Length || count <= 0) return stem;
        count = Math.Min(count, stem.Length - start);
        return stem.Remove(start, count);
    }

    /// <summary>Removes the first From up to the next To after it; no-op when either is absent.</summary>
    internal static string ApplyRemoveBetween(string s, RemoveBetweenRule rule)
    {
        if (!rule.Enabled || rule.From.Length == 0 || rule.To.Length == 0) return s;
        int from = s.IndexOf(rule.From, StringComparison.Ordinal);
        if (from < 0) return s;
        int to = s.IndexOf(rule.To, from + rule.From.Length, StringComparison.Ordinal);
        if (to < 0) return s;
        return rule.IncludeDelimiters
            ? s.Remove(from, to + rule.To.Length - from)
            : s.Remove(from + rule.From.Length, to - from - rule.From.Length);
    }

    internal static string RemoveCameraPrefixes(string s)
    {
        bool stripped;
        do
        {
            stripped = false;
            foreach (var prefix in CameraPrefixes)
            {
                if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                s = s[prefix.Length..];
                stripped = true;
                break;
            }
        } while (stripped && s.Length > 0);
        return s;
    }

    internal static string RemoveDatePatterns(string s) => DateDmy.Replace(DateYmd.Replace(s, ""), "");

    internal static string RemoveGuidPatterns(string s) => GuidRun.Replace(s, "");

    internal static string RemoveUrls(string s) => UrlRun.Replace(s, "");

    /// <summary>Removes each listed word (comma/space separated) as a whole word, case-insensitively.</summary>
    internal static string RemoveWords(string s, string words)
    {
        var escaped = SplitWords(words).Select(Regex.Escape).ToList();
        if (escaped.Count == 0) return s;
        return Regex.Replace(s, @"\b(?:" + string.Join("|", escaped) + @")\b", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string RemoveBracketed(string stem)
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

    internal static string RemoveLeadingDigits(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return s[i..];
    }

    internal static string RemoveTrailingDigits(string s)
    {
        int j = s.Length;
        while (j > 0 && char.IsDigit(s[j - 1])) j--;
        return s[..j];
    }

    /// <summary>Strips Unicode punctuation and symbol characters, keeping - _ . (and spaces).</summary>
    internal static string RemovePunctuation(string s) =>
        new([.. s.Where(c => c is '-' or '_' or '.' || !(char.IsPunctuation(c) || char.IsSymbol(c)))]);

    internal static string RemoveNonAscii(string s) => new([.. s.Where(c => c <= 0x7F)]);

    /// <summary>
    /// Drops surrogate halves (all non-BMP characters, which is where most emoji live), the
    /// BMP emoji blocks U+2600–U+27BF, the emoji variation selector U+FE0F and the zero-width
    /// joiner U+200D that only appears inside emoji sequences.
    /// </summary>
    internal static string RemoveEmoji(string s) =>
        new([.. s.Where(c => !char.IsSurrogate(c) && c != '\uFE0F' && c != '\u200D'
                             && (c < '\u2600' || c > '\u27BF'))]);

    // ---------- inserts ----------

    internal static string ApplyInsert(string s, InsertRule rule)
    {
        if (!rule.Enabled || rule.Text.Length == 0) return s;
        if (rule.Anchor is InsertAnchor.BeforeText or InsertAnchor.AfterText)
        {
            if (rule.AnchorText.Length == 0) return s;
            int idx = s.IndexOf(rule.AnchorText, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return s; // anchor absent: no-op
            return s.Insert(rule.Anchor == InsertAnchor.BeforeText ? idx : idx + rule.AnchorText.Length, rule.Text);
        }
        int position = rule.FromEnd ? s.Length - rule.Position : rule.Position;
        return s.Insert(Math.Clamp(position, 0, s.Length), rule.Text);
    }

    // ---------- hygiene ----------

    /// <summary>Zero-pads every digit run shorter than the width; longer runs are unchanged.</summary>
    internal static string PadNumberRuns(string s, int width) =>
        DigitRuns.Replace(s, m => m.Value.Length >= width ? m.Value : m.Value.PadLeft(width, '0'));

    internal static string CollapseRepeatedSeparators(string s) => SeparatorRun.Replace(s, "$1");

    internal static string TrimSeparators(string s) => s.Trim(SeparatorChars);

    internal static string RemoveDiacritics(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Superset of <see cref="RemoveDiacritics"/>: best-effort common mappings first
    /// (ß→ss, æ→ae, ø→o, ...), then the diacritic fold, then drop whatever is still non-ASCII.
    /// </summary>
    internal static string TransliterateToAscii(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case 'ß': sb.Append("ss"); break;
                case 'ẞ': sb.Append("SS"); break;
                case 'æ': sb.Append("ae"); break;
                case 'Æ': sb.Append("AE"); break;
                case 'œ': sb.Append("oe"); break;
                case 'Œ': sb.Append("OE"); break;
                case 'ø': sb.Append('o'); break;
                case 'Ø': sb.Append('O'); break;
                case 'đ': sb.Append('d'); break;
                case 'Đ': sb.Append('D'); break;
                case 'ł': sb.Append('l'); break;
                case 'Ł': sb.Append('L'); break;
                case 'þ': sb.Append("th"); break;
                case 'Þ': sb.Append("Th"); break;
                case 'ð': sb.Append('d'); break;
                case 'Ð': sb.Append('D'); break;
                default: sb.Append(c); break;
            }
        }
        return RemoveNonAscii(RemoveDiacritics(sb.ToString()));
    }

    /// <summary>End cuts the tail, Start the head; Middle keeps head+tail halves joined directly (no ellipsis).</summary>
    internal static string Truncate(string s, int max, TruncateFrom from)
    {
        if (max <= 0 || s.Length <= max) return s;
        if (from == TruncateFrom.Start) return s[^max..];
        if (from == TruncateFrom.End) return s[..max];
        int head = (max + 1) / 2;
        int tail = max - head;
        return tail > 0 ? s[..head] + s[^tail..] : s[..head];
    }

    // ---------- case ----------

    /// <summary>NameCase plus the smart-title-case and preserve-case-words refinements.</summary>
    internal static string ApplyNameCase(string stem, RenameOptions o)
    {
        var result = o.NameCase == CaseTransform.TitleCase && o.SmartTitleCase
            ? SmartTitleCase(stem, o.SmallWords)
            : ApplyCase(stem, o.NameCase);
        if (o.NameCase != CaseTransform.None && !string.IsNullOrWhiteSpace(o.PreserveCaseWords))
            result = RestorePreservedCase(result, o.PreserveCaseWords);
        return result;
    }

    internal static string ApplyCase(string s, CaseTransform transform) => transform switch
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
        CaseTransform.PascalCase => PascalOrCamel(s, upperFirst: true),
        CaseTransform.CamelCase => PascalOrCamel(s, upperFirst: false),
        CaseTransform.SnakeCase => SeparatorCase(s, "_"),
        CaseTransform.KebabCase => SeparatorCase(s, "-"),
        CaseTransform.RandomCase => RandomizeCase(s),
        _ => s,
    };

    /// <summary>TitleCase that keeps the listed small words lowercase unless first or last word.</summary>
    internal static string SmartTitleCase(string s, string smallWords)
    {
        var small = new HashSet<string>(SplitWords(smallWords), StringComparer.OrdinalIgnoreCase);
        var matches = WordTokens.Matches(s);
        if (matches.Count == 0) return s;
        var sb = new StringBuilder(s.Length);
        int prev = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            sb.Append(s, prev, m.Index - prev);
            var lower = m.Value.ToLowerInvariant();
            bool keepSmall = i != 0 && i != matches.Count - 1 && small.Contains(lower);
            sb.Append(keepSmall ? lower : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower));
            prev = m.Index + m.Length;
        }
        sb.Append(s, prev, s.Length - prev);
        return sb.ToString();
    }

    /// <summary>Words that case-insensitively match a listed word get the listed (typed) casing back.</summary>
    internal static string RestorePreservedCase(string s, string preserveWords)
    {
        var typed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in SplitWords(preserveWords)) typed.TryAdd(w, w);
        return typed.Count == 0 ? s : WordTokens.Replace(s, m => typed.TryGetValue(m.Value, out var t) ? t : m.Value);
    }

    private static string PascalOrCamel(string s, bool upperFirst)
    {
        var sb = new StringBuilder(s.Length);
        bool first = true;
        foreach (Match m in WordTokens.Matches(s))
        {
            var word = m.Value.ToLowerInvariant();
            if (first && !upperFirst) sb.Append(word);
            else sb.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
            first = false;
        }
        return sb.ToString();
    }

    private static string SeparatorCase(string s, string separator) =>
        Regex.Replace(s, @"[-_. ]+", separator, Rx).ToLowerInvariant();

    /// <summary>
    /// Deterministic rAnDoM cAsE: the seed is a stable hash of the input, so the same name
    /// always produces the same output (previews never flicker between plan rebuilds).
    /// </summary>
    internal static string RandomizeCase(string s)
    {
        if (s.Length == 0) return s;
        uint state = 2166136261u; // FNV-1a over the chars — platform-independent, unlike GetHashCode
        foreach (var c in s) state = unchecked((state ^ c) * 16777619u);
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            state = unchecked(state * 1664525u + 1013904223u); // LCG step per character
            if (!char.IsLetter(chars[i])) continue;
            chars[i] = ((state >> 16) & 1) == 0
                ? char.ToLowerInvariant(chars[i])
                : char.ToUpperInvariant(chars[i]);
        }
        return new string(chars);
    }

    // ---------- shared ----------

    /// <summary>Extension text may carry replacement output; keep only Windows-legal characters.</summary>
    internal static string SanitizeExtension(string ext) =>
        new string([.. ext.Where(c => c >= 0x20 && !InvalidNameChars.Contains(c))]).Trim(' ', '.');

    internal static string[] SplitWords(string list) =>
        list.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
