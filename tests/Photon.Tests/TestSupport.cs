using System.Globalization;
using System.Text;
using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

/// <summary>
/// Unique temp root per test instance, deleted on dispose. File content comes from a
/// Random(1234) stream so runs are deterministic (nothing time- or order-dependent
/// across runs: xunit creates one test-class instance per test method).
/// </summary>
public sealed class TempDir : IDisposable
{
    private readonly Random _rng = new(1234);

    public string Root { get; }

    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "photon-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Absolute path under the root (segments may use '/' for portability).</summary>
    public string At(params string[] segments) =>
        Path.Combine([Root, .. segments.SelectMany(s => s.Split('/'))]);

    /// <summary>Creates (and returns) a directory under the root.</summary>
    public string Dir(params string[] segments)
    {
        var p = At(segments);
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Deterministic pseudo-random bytes from the seeded stream.</summary>
    public byte[] Bytes(int count)
    {
        var b = new byte[count];
        _rng.NextBytes(b);
        return b;
    }

    /// <summary>Creates a file with deterministic random content; returns its absolute path.</summary>
    public string File(string relativePath, int sizeBytes = 256) => File(relativePath, Bytes(sizeBytes));

    public string File(string relativePath, byte[] content)
    {
        var p = At(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllBytes(p, content);
        return p;
    }

    public string TextFile(string relativePath, string content)
    {
        var p = At(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}

/// <summary>Pins the current culture so number and month-name formatting is deterministic.</summary>
public abstract class InvariantCultureTest
{
    protected InvariantCultureTest()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}

/// <summary>
/// Single construction point for the concrete services. If a constructor signature
/// differs from these guesses, this is the only file the integrator needs to touch.
/// </summary>
internal static class TestServices
{
    public static JournalService Journal(string directory) => new(directory);
    public static FileScanner FileScanner() => new();
    public static MetadataReader MetadataReader() => new();
    public static DateResolver DateResolver() => new();
    public static SortPlanner Planner() => new(new MetadataReader(), new DateResolver());
    public static SortExecutor Executor(JournalService journal) => new(journal);
    public static RenameEngine RenameEngine(JournalService journal) => new(journal);
    public static DuplicateFinder DuplicateFinder(JournalService journal) => new(journal);
    public static FolderFlattener Flattener(JournalService journal) => new(journal);
    public static FolderScanner FolderScanner() => new();
}

/// <summary>Hand-built MediaFile instances with sensible defaults.</summary>
internal static class Media
{
    public static readonly DateTime DefaultStamp = new(2023, 3, 5, 14, 30, 22);

    public static MediaFile File(string path, long size = 1000,
        DateTime? created = null, DateTime? modified = null, DateTime? exif = null,
        string? make = null, string? model = null, bool isVideo = false)
        => new()
        {
            FilePath = path,
            SizeBytes = size,
            FileCreated = created ?? DefaultStamp,
            FileModified = modified ?? DefaultStamp,
            IsVideo = isVideo,
            ExifDate = exif,
            CameraMake = make,
            CameraModel = model,
            MetadataLoaded = true,
        };
}

/// <summary>IProgress that invokes the handler synchronously (Progress&lt;T&gt; posts async — useless for deterministic cancellation).</summary>
public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

public static class AssertEx
{
    /// <summary>Timestamps compared at whole-second precision (FAT/exFAT and copy APIs may drop sub-second ticks).</summary>
    public static void SameSecond(DateTime expected, DateTime actual)
    {
        Assert.Equal(Trunc(expected), Trunc(actual));
        static long Trunc(DateTime d) => d.Ticks - d.Ticks % TimeSpan.TicksPerSecond;
    }
}

/// <summary>Minimal RFC-4180 CSV line parser for asserting quoting behavior.</summary>
public static class MiniCsv
{
    public static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
