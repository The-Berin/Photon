using Photon.Core.Models;

namespace Photon.Core.Util;

public static class SizeFormatter
{
    private static readonly string[] AutoUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Formats a byte count per the user's size-unit preference ("296.62 MB").</summary>
    public static string Format(long bytes, SizeUnit unit = SizeUnit.Auto)
    {
        return unit switch
        {
            SizeUnit.Bytes => $"{bytes:N0} B",
            SizeUnit.KB => $"{bytes / 1024d:N2} KB",
            SizeUnit.MB => $"{bytes / (1024d * 1024):N2} MB",
            SizeUnit.GB => $"{bytes / (1024d * 1024 * 1024):N2} GB",
            SizeUnit.TB => $"{bytes / (1024d * 1024 * 1024 * 1024):N2} TB",
            _ => FormatAuto(bytes),
        };
    }

    private static string FormatAuto(long bytes)
    {
        if (bytes < 0) return "-" + FormatAuto(-bytes);
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < AutoUnits.Length - 1) { value /= 1024; i++; }
        return i == 0 ? $"{bytes:N0} B" : $"{value:N2} {AutoUnits[i]}";
    }

    public static string FormatRate(double bytesPerSecond) => $"{FormatAuto((long)bytesPerSecond)}/s";
}
