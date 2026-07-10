using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Core.Services;

/// <summary>Expands the rename pattern tokens documented on <see cref="RenameOptions"/>.</summary>
internal static class RenameTokens
{
    // Tokens are case-sensitive: {MM} is the month, {mm} the minute.
    private static readonly Regex TokenRegex = new(@"\{([A-Za-z0-9]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string Alphanumeric = "abcdefghijklmnopqrstuvwxyz0123456789";

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
}

/// <summary>
/// Per-file token state. Metadata, date resolution and content hashing are all lazy so a
/// plan only opens files when the pattern actually asks for something stored inside them.
/// </summary>
internal sealed class RenameTokenContext(string filePath, int counterValue, RenameOptions options,
    IMetadataReader metadata, IDateResolver dates)
{
    private FileInfo? _info;
    private MediaFile? _media;
    private bool _dateResolved;
    private DateTime? _date;
    private string? _hash8;

    /// <summary>Set when a token could not be produced (no resolvable date, unreadable file).</summary>
    public string? Problem { get; private set; }

    public string? Resolve(string token) => token switch
    {
        "name" => Path.GetFileNameWithoutExtension(filePath),
        "ext" => Path.GetExtension(filePath) is { Length: > 1 } e ? e[1..] : "",
        "counter" => counterValue.ToString(CultureInfo.InvariantCulture)
            .PadLeft(Math.Max(0, options.CounterPadding), '0'),
        "yyyy" or "yy" or "MM" or "MMM" or "MMMM" or "dd" or "ddd" or "HH" or "mm" or "ss"
            => FormatDate(token),
        "date" => FormatDate("yyyy-MM-dd"),
        "time" => FormatDate("HH-mm-ss"),
        "camera" => $"{Media.CameraMake} {Media.CameraModel}".Trim(),
        "make" => Media.CameraMake ?? "",
        "model" => Media.CameraModel ?? "",
        "width" => Media.PixelWidth?.ToString(CultureInfo.InvariantCulture) ?? "",
        "height" => Media.PixelHeight?.ToString(CultureInfo.InvariantCulture) ?? "",
        "mp" => Megapixels(),
        "size" => SizeFormatter.Format(FileLength()),
        "sizeMB" => (FileLength() / (1024d * 1024)).ToString("0.00", CultureInfo.InvariantCulture),
        "parent" => ParentName(1),
        "parent2" => ParentName(2),
        "hash8" => Hash8(),
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

    private string FormatDate(string format)
    {
        if (!_dateResolved)
        {
            _dateResolved = true;
            // FileDateOnly never consults EXIF, so skip the metadata read entirely.
            var media = options.DateSource == DateSource.FileDateOnly ? RawMedia : Media;
            _date = dates.Resolve(media, options.DateSource).Date;
        }
        if (_date is not DateTime d)
        {
            Problem ??= $"no date available ({options.DateSource})";
            return "";
        }
        return d.ToString(format, CultureInfo.InvariantCulture);
    }

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

    private string Hash8()
    {
        if (_hash8 is null)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                _hash8 = Convert.ToHexString(SHA256.HashData(fs))[..8].ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Problem ??= $"cannot hash file: {ex.Message}";
                _hash8 = "";
            }
        }
        return _hash8;
    }
}
