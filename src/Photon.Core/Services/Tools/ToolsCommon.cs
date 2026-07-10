using System.Diagnostics;
using System.Text.RegularExpressions;
using Photon.Core.Models;

namespace Photon.Core.Services;

/// <summary>Shared plumbing for the tools engines (rename, duplicates, flatten, scan).</summary>
internal static class ToolsCommon
{
    /// <summary>Every default media extension (pictures + videos), with dot, case-insensitive.</summary>
    internal static readonly HashSet<string> MediaExtensions = new(
        [.. ScanFilter.DefaultPictureExtensions, .. ScanFilter.DefaultVideoExtensions],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Compiles a wildcard mask ("IMG_*.jpg;*.png") to a regex; null when the mask is blank.</summary>
    internal static Regex? CompileMask(string mask)
    {
        if (string.IsNullOrWhiteSpace(mask)) return null;
        var alternatives = mask
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => Regex.Escape(m).Replace(@"\*", ".*").Replace(@"\?", "."))
            .ToList();
        if (alternatives.Count == 0) return null;
        return new Regex("^(?:" + string.Join("|", alternatives) + ")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Depth-first file enumeration that skips unreadable directories instead of throwing.</summary>
    internal static IEnumerable<string> EnumerateFilesSafe(string root, bool recursive, Action<string>? onInaccessible = null)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { onInaccessible?.Invoke(dir); files = []; }
            foreach (var file in files) yield return file;

            if (!recursive) yield break; // the first popped dir is the root itself

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { onInaccessible?.Invoke(dir); subs = []; }
            foreach (var sub in subs) pending.Push(sub);
        }
    }

    /// <summary>Every directory below root (root itself excluded); unreadable subtrees are skipped.</summary>
    internal static List<string> EnumerateDirectoriesSafe(string root)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var sub in subs)
            {
                result.Add(sub);
                pending.Push(sub);
            }
        }
        return result;
    }

    internal static SortProgress MakeProgress(string currentFile, int processed, int total,
        long processedBytes, long totalBytes, Stopwatch stopwatch)
    {
        var seconds = stopwatch.Elapsed.TotalSeconds;
        var filesPerSecond = seconds > 0 ? processed / seconds : 0;
        var bytesPerSecond = seconds > 0 ? processedBytes / seconds : 0;
        TimeSpan? remaining = filesPerSecond > 0 && total > processed
            ? TimeSpan.FromSeconds((total - processed) / filesPerSecond)
            : null;
        return new SortProgress
        {
            CurrentFile = currentFile,
            ProcessedCount = processed,
            TotalCount = total,
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes,
            FilesPerSecond = filesPerSecond,
            BytesPerSecond = bytesPerSecond,
            EstimatedRemaining = remaining,
        };
    }

    /// <summary>
    /// Where JournalService.SaveAsync writes this journal. Delegates to the shared
    /// JournalService naming convention so reported JournalPath values are accurate.
    /// </summary>
    internal static string JournalFilePath(IJournalService journalService, SortJournal journal) =>
        Path.Combine(journalService.JournalDirectory, JournalService.FileNameFor(journal));
}
