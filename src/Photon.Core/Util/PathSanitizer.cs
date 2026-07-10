namespace Photon.Core.Util;

/// <summary>
/// Windows path hygiene for generated folder/file names (camera models, rename patterns, ...).
/// Note: targets Windows rules even when running on other OSes (tests run on macOS/Linux CI).
/// </summary>
public static class PathSanitizer
{
    private static readonly char[] InvalidNameChars = ['"', '<', '>', '|', ':', '*', '?', '\\', '/'];
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Makes a single path segment safe on Windows: strips invalid/control chars, trailing dots/spaces, reserved names.</summary>
    public static string SanitizeSegment(string segment, string fallback = "_")
    {
        if (string.IsNullOrWhiteSpace(segment)) return fallback;
        var chars = segment.Where(c => c >= 0x20 && !InvalidNameChars.Contains(c)).ToArray();
        var cleaned = new string(chars).Trim().TrimEnd('.', ' ');
        if (cleaned.Length == 0) return fallback;
        var stem = cleaned.Split('.')[0];
        if (ReservedNames.Contains(stem)) cleaned = "_" + cleaned;
        return cleaned;
    }

    /// <summary>Appends _1, _2, ... before the extension until the name doesn't collide, per the given exists-check.</summary>
    public static string MakeUnique(string fullPath, Func<string, bool> exists)
    {
        if (!exists(fullPath)) return fullPath;
        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);
        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!exists(candidate)) return candidate;
        }
    }
}
