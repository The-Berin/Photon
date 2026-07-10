using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>Expands the rename pattern tokens documented on <see cref="RenameOptions"/>.</summary>
internal static class RenameTokens
{
    // Tokens are case-sensitive: {MM} is the month, {mm} the minute. Hyphens allowed ({age-days}).
    private static readonly Regex TokenRegex = new(@"\{([A-Za-z0-9-]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string Alphanumeric = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    /// <summary>Replaces known tokens; unknown ones stay verbatim.</summary>
    internal static string Expand(string pattern, RenameTokenContext ctx)
        => TokenRegex.Replace(pattern, m => ctx.Resolve(m.Groups[1].Value) ?? m.Value);

    internal static string RandomAlphanumeric(int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = Alphanumeric[Random.Shared.Next(Alphanumeric.Length)];
        return new string(chars);
    }

    /// <summary>
    /// Renders a counter value in the configured style. Numeric and hex respect the padding;
    /// alpha and roman are naturally variable-width. Zero/negative values that a style cannot
    /// represent (roman 0, alpha 0) fall back to plain digits.
    /// </summary>
    internal static string RenderCounter(int value, CounterStyle style, int padding) => style switch
    {
        CounterStyle.AlphaLower => ToAlpha(value),
        CounterStyle.AlphaUpper => ToAlpha(value).ToUpperInvariant(),
        CounterStyle.RomanLower => ToRoman(value).ToLowerInvariant(),
        CounterStyle.RomanUpper => ToRoman(value),
        CounterStyle.HexLower => ToHex(value, upper: false, padding),
        CounterStyle.HexUpper => ToHex(value, upper: true, padding),
        _ => value.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(0, padding), '0'),
    };

    /// <summary>Streaming CRC-32 (IEEE 802.3, reflected, poly 0xEDB88320) as 8 lowercase hex chars.</summary>
    internal static string Crc32Hex(Stream stream)
    {
        uint crc = 0xFFFFFFFFu;
        var buffer = new byte[1 << 16];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++)
                crc = Crc32Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        return (crc ^ 0xFFFFFFFFu).ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>Bijective base-26: 1→a, 26→z, 27→aa, 28→ab, ...</summary>
    private static string ToAlpha(int n)
    {
        if (n <= 0) return n.ToString(CultureInfo.InvariantCulture);
        var chars = new Stack<char>();
        while (n > 0)
        {
            n--;
            chars.Push((char)('a' + n % 26));
            n /= 26;
        }
        return new string([.. chars]);
    }

    private static string ToRoman(int n)
    {
        if (n <= 0) return n.ToString(CultureInfo.InvariantCulture); // no roman zero
        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        ReadOnlySpan<string> symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length; i++)
            while (n >= values[i])
            {
                sb.Append(symbols[i]);
                n -= values[i];
            }
        return sb.ToString();
    }

    private static string ToHex(int n, bool upper, int padding)
    {
        if (n < 0) return n.ToString(CultureInfo.InvariantCulture);
        return n.ToString(upper ? "X" : "x", CultureInfo.InvariantCulture).PadLeft(Math.Max(0, padding), '0');
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}

/// <summary>
/// Per-file token state. Metadata, date resolution and content hashing are all lazy so a
/// plan only opens files when the pattern actually asks for something stored inside them.
/// </summary>
internal sealed class RenameTokenContext(string filePath, int counterValue, int counter2Value,
    RenameOptions options, IMetadataReader metadata, IDateResolver dates)
{
    private FileInfo? _info;
    private MediaFile? _media;
    private bool _dateResolved;
    private DateTime? _date;
    private string? _hash8;
    private string? _md58;
    private string? _sha18;
    private string? _crc32;

    /// <summary>Set when a token could not be produced (no resolvable date, unreadable file).</summary>
    public string? Problem { get; private set; }

    public string? Resolve(string token) => token switch
    {
        // ----- name / file -----
        "name" => Path.GetFileNameWithoutExtension(filePath),
        "ext" or "origext" => Path.GetExtension(filePath) is { Length: > 1 } e ? e[1..] : "",
        "parent" => ParentName(1),
        "parent2" => ParentName(2),
        "parent3" => ParentName(3),
        "drive" => DriveName(),
        "depth" => Depth(),
        "size" => SizeFormatter.Format(FileLength()),
        "sizeMB" => (FileLength() / (1024d * 1024)).ToString("0.00", CultureInfo.InvariantCulture),
        "filesize-bytes" => FileLength().ToString(CultureInfo.InvariantCulture),

        // ----- counters -----
        "counter" => RenameTokens.RenderCounter(counterValue, options.CounterStyle, options.CounterPadding),
        "counter2" => RenameTokens.RenderCounter(counter2Value, options.CounterStyle, options.Counter2Padding),

        // ----- date / time -----
        "yyyy" or "yy" or "MM" or "MMM" or "MMMM" or "dd" or "ddd" or "HH" or "mm" or "ss"
            => FormatDate(token),
        "date" => FormatDate("yyyy-MM-dd"),
        "time" => FormatDate("HH-mm-ss"),
        "hh12" => FormatDate("hh"),
        "ampm" => FormatDate("tt"),
        "weekday" => FormatDate("dddd"),
        "week" => DatePart(d => ISOWeek.GetWeekOfYear(d).ToString("00", CultureInfo.InvariantCulture)),
        "quarter" => DatePart(d => "Q" + ((d.Month + 2) / 3).ToString(CultureInfo.InvariantCulture)),
        "dayofyear" => DatePart(d => d.DayOfYear.ToString("000", CultureInfo.InvariantCulture)),
        // The stamp is treated as UTC so the value never depends on the machine's timezone.
        "epoch" => DatePart(d => ((DateTimeOffset)DateTime.SpecifyKind(d, DateTimeKind.Utc))
            .ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        "age-days" => DatePart(d => Math.Max(0, (int)(DateTime.Now - d).TotalDays)
            .ToString(CultureInfo.InvariantCulture)),

        // ----- camera / EXIF -----
        "camera" => $"{Media.CameraMake} {Media.CameraModel}".Trim(),
        "make" => Media.CameraMake ?? "",
        "model" => Media.CameraModel ?? "",
        "lens" => Media.LensModel ?? "",
        "artist" => Media.Artist ?? "",
        "software" => Media.Software ?? "",
        "orientation" => Media.Orientation?.ToString(CultureInfo.InvariantCulture) ?? "",
        "width" => Media.PixelWidth?.ToString(CultureInfo.InvariantCulture) ?? "",
        "height" => Media.PixelHeight?.ToString(CultureInfo.InvariantCulture) ?? "",
        "mp" => Megapixels(),
        "fnumber" => Media.FNumber is double f ? "f" + f.ToString("0.#", CultureInfo.InvariantCulture) : "",
        "iso-speed" => Media.IsoSpeed?.ToString(CultureInfo.InvariantCulture) ?? "",
        "exposure" => Media.ExposureTime?.Replace('/', '-') ?? "", // "1/250" → filename-safe "1-250"
        "focal" => Media.FocalLengthMm is double mm
            ? Math.Round(mm).ToString("0", CultureInfo.InvariantCulture) : "",

        // ----- video -----
        "duration" => Duration(minutesDotSeconds: true),
        "duration-s" => Duration(minutesDotSeconds: false),

        // ----- GPS -----
        "lat" => Media.GpsLatitude?.ToString("0.00000", CultureInfo.InvariantCulture) ?? "",
        "lon" => Media.GpsLongitude?.ToString("0.00000", CultureInfo.InvariantCulture) ?? "",
        "gps" => Media is { GpsLatitude: double la, GpsLongitude: double lo }
            ? la.ToString("0.00000", CultureInfo.InvariantCulture) + ","
              + lo.ToString("0.00000", CultureInfo.InvariantCulture)
            : "",

        // ----- identity -----
        "hash8" => ContentHash(ref _hash8, s => Convert.ToHexString(SHA256.HashData(s))[..8].ToLowerInvariant()),
        "md5-8" => ContentHash(ref _md58, s => Convert.ToHexString(MD5.HashData(s))[..8].ToLowerInvariant()),
        "sha1-8" => ContentHash(ref _sha18, s => Convert.ToHexString(SHA1.HashData(s))[..8].ToLowerInvariant()),
        "crc32" => ContentHash(ref _crc32, RenameTokens.Crc32Hex),
        "guid" => Guid.NewGuid().ToString("N"),
        "rand4" => RenameTokens.RandomAlphanumeric(4),
        "rand8" => RenameTokens.RandomAlphanumeric(8),

        _ => null, // unknown token: caller leaves it verbatim
    };

    private FileInfo Info => _info ??= new FileInfo(filePath);

    private long FileLength()
    {
        try { return Info.Length; }
        catch { Problem ??= "cannot read file size"; return 0; }
    }

    /// <summary>The MediaFile shell without EXIF loaded (enough for file-date-only policies).</summary>
    private MediaFile RawMedia => _media ??= new MediaFile
    {
        FilePath = filePath,
        SizeBytes = FileLength(),
        FileCreated = Info.CreationTime,
        FileModified = Info.LastWriteTime,
        IsVideo = ScanFilter.DefaultVideoExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant()),
    };

    private MediaFile Media
    {
        get
        {
            var m = RawMedia;
            if (!m.MetadataLoaded) metadata.Populate(m);
            return m;
        }
    }

    private DateTime? ResolvedDate()
    {
        if (!_dateResolved)
        {
            _dateResolved = true;
            // FileDateOnly never consults EXIF, so skip the metadata read entirely.
            var media = options.DateSource == DateSource.FileDateOnly ? RawMedia : Media;
            _date = dates.Resolve(media, options.DateSource).Date;
        }
        if (_date is null) Problem ??= $"no date available ({options.DateSource})";
        return _date;
    }

    private string DatePart(Func<DateTime, string> format) =>
        ResolvedDate() is DateTime d ? format(d) : "";

    private string FormatDate(string format) =>
        DatePart(d => d.ToString(format, CultureInfo.InvariantCulture));

    private string Megapixels()
    {
        if (Media.PixelWidth is not int w || Media.PixelHeight is not int h) return "";
        return (w * (double)h / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private string ParentName(int levels)
    {
        var dir = Path.GetDirectoryName(filePath);
        for (int i = 1; i < levels && dir is not null; i++) dir = Path.GetDirectoryName(dir);
        return dir is null ? "" : Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
    }

    /// <summary>"C:\" → "C"; UNC and rootless paths degrade to whatever remains after trimming.</summary>
    private string DriveName()
    {
        string root;
        try { root = Path.GetPathRoot(Path.GetFullPath(filePath)) ?? ""; }
        catch { return ""; }
        return root.TrimEnd('\\', '/').TrimEnd(':');
    }

    /// <summary>Directory nesting depth below the root: "C:\a\b\c.jpg" → 2.</summary>
    private string Depth()
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? "";
            var root = Path.GetPathRoot(dir) ?? "";
            int depth = dir[root.Length..]
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Length;
            return depth.ToString(CultureInfo.InvariantCulture);
        }
        catch { return ""; }
    }

    /// <summary>"{duration}" renders 155s as "2.35" (m.ss); "{duration-s}" as whole seconds.</summary>
    private string Duration(bool minutesDotSeconds)
    {
        if (Media.DurationSeconds is not double seconds) return "";
        int total = (int)Math.Round(seconds);
        if (!minutesDotSeconds) return total.ToString(CultureInfo.InvariantCulture);
        return (total / 60).ToString(CultureInfo.InvariantCulture) + "."
             + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    private string ContentHash(ref string? cache, Func<Stream, string> compute)
    {
        if (cache is null)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                cache = compute(fs);
            }
            catch (Exception ex)
            {
                Problem ??= $"cannot hash file: {ex.Message}";
                cache = "";
            }
        }
        return cache;
    }
}
