using System.Text;

namespace Photon.Core.Services;

/// <summary>
/// Magic-byte detection for FixExtensionBySniffing plus the NormalizeExtensions synonym map.
/// Reads at most 16 bytes per file and only when asked (the rename plan stays lazy).
/// </summary>
internal static class ExtensionSniffer
{
    /// <summary>
    /// Extensions each sniffed type accepts as already-correct. Deliberately generous:
    /// the TIFF entry includes the TIFF-based RAW formats (CR2/NEF/DNG/...) so sniffing
    /// never "corrects" a camera RAW file to .tif, and .webm keeps its name even though
    /// the EBML signature alone cannot distinguish it from .mkv.
    /// </summary>
    private static readonly Dictionary<string, string[]> AcceptedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = ["jpg", "jpeg", "jpe", "jfif"],
        ["png"] = ["png"],
        ["gif"] = ["gif"],
        ["bmp"] = ["bmp", "dib"],
        ["tif"] = ["tif", "tiff", "cr2", "nef", "dng", "arw", "srw", "pef", "raw"],
        ["heic"] = ["heic", "heif", "hif"],
        ["mp4"] = ["mp4", "m4v"],
        ["mov"] = ["mov", "qt"],
        ["avi"] = ["avi"],
        ["mkv"] = ["mkv", "webm"],
        ["webp"] = ["webp"],
    };

    /// <summary>Sniffs the file's magic bytes; null when unreadable or not confidently recognized.</summary>
    internal static string? Sniff(string path)
    {
        var header = new byte[16];
        int read;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            read = fs.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        }
        catch
        {
            return null; // unreadable file: never a confident sniff
        }
        return Match(header, read);
    }

    /// <summary>True when the current extension is an accepted spelling of the sniffed type.</summary>
    internal static bool MatchesSniffed(string sniffed, string currentExt) =>
        AcceptedExtensions.TryGetValue(sniffed, out var accepted)
        && accepted.Contains(currentExt.TrimStart('.'), StringComparer.OrdinalIgnoreCase);

    /// <summary>The NormalizeExtensions synonym map: jpeg→jpg, tiff→tif, mpeg→mpg, htm→html.</summary>
    internal static string Normalize(string ext) => ext.ToLowerInvariant() switch
    {
        "jpeg" => "jpg",
        "tiff" => "tif",
        "mpeg" => "mpg",
        "htm" => "html",
        _ => ext,
    };

    private static string? Match(byte[] h, int len)
    {
        if (len >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpg";
        if (len >= 4 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return "png";
        if (len >= 3 && h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46) return "gif";
        if (len >= 4 && ((h[0] == 0x49 && h[1] == 0x49 && h[2] == 0x2A && h[3] == 0x00)
                      || (h[0] == 0x4D && h[1] == 0x4D && h[2] == 0x00 && h[3] == 0x2A))) return "tif";
        if (len >= 4 && h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3) return "mkv";
        if (len >= 12 && Ascii(h, 0) == "RIFF")
        {
            var form = Ascii(h, 8);
            if (form == "AVI ") return "avi";
            if (form == "WEBP") return "webp";
            return null;
        }
        if (len >= 12 && Ascii(h, 4) == "ftyp")
        {
            return Ascii(h, 8) switch
            {
                "heic" or "heix" or "hevc" or "mif1" => "heic",
                "isom" or "mp42" or "mp41" or "avc1" or "M4V " => "mp4",
                "qt  " => "mov",
                _ => null, // unfamiliar brand: not confident enough to correct anything
            };
        }
        // "BM" is only two bytes — weakest signature, so it is checked last.
        if (len >= 2 && h[0] == 0x42 && h[1] == 0x4D) return "bmp";
        return null;
    }

    private static string Ascii(byte[] h, int offset) => Encoding.ASCII.GetString(h, offset, 4);
}
