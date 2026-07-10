namespace Photon.Core.Models;

/// <summary>
/// One scanned picture or video file, with everything the planner needs to know about it.
/// Populated by IFileScanner; EXIF fields filled by IMetadataReader.
/// </summary>
public sealed class MediaFile
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
    public long SizeBytes { get; init; }
    public DateTime FileCreated { get; init; }
    public DateTime FileModified { get; init; }
    public bool IsVideo { get; init; }

    // EXIF / container metadata (null until IMetadataReader.Populate runs, or when absent in the file)
    public DateTime? ExifDate { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public int? PixelWidth { get; set; }
    public int? PixelHeight { get; set; }
    public bool MetadataLoaded { get; set; }
}

/// <summary>The date chosen for a file under a given DateSource policy.</summary>
/// <param name="Date">Null when the policy could not produce a date (e.g. ExifOnly and no EXIF).</param>
/// <param name="FromExif">True when the date came from EXIF/container metadata.</param>
public readonly record struct DateResolution(DateTime? Date, bool FromExif);
