using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;
using Photon.Core.Models;
using MetaDirectory = MetadataExtractor.Directory;

namespace Photon.Core.Services;

/// <summary>Default <see cref="IMetadataReader"/> built on the MetadataExtractor library.</summary>
public sealed class MetadataReader : IMetadataReader
{
    // QuickTime's epoch is 1904 and cameras with dead clocks emit 1904/1970 stamps;
    // anything before 1990 predates consumer digital cameras and is treated as absent.
    private static readonly DateTime EarliestPlausibleDate = new(1990, 1, 1);

    public void Populate(MediaFile file)
    {
        try
        {
            IReadOnlyList<MetaDirectory> dirs = ImageMetadataReader.ReadMetadata(file.FilePath);
            file.ExifDate = file.IsVideo ? ReadVideoDate(dirs) : ReadPhotoDate(dirs);
            ReadCamera(file, dirs);
            ReadGps(file, dirs);
            ReadDimensions(file, dirs);
            ReadExtended(file, dirs);
        }
        catch
        {
            // Corrupt or unsupported file: every metadata field simply stays null.
        }
        finally
        {
            file.MetadataLoaded = true;
        }
    }

    private static DateTime? ReadPhotoDate(IReadOnlyList<MetaDirectory> dirs)
    {
        foreach (var sub in dirs.OfType<ExifSubIfdDirectory>())
            if (sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var original))
                return original;
        foreach (var ifd0 in dirs.OfType<ExifIfd0Directory>())
            if (ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dateTime))
                return dateTime;
        return null;
    }

    private static DateTime? ReadVideoDate(IReadOnlyList<MetaDirectory> dirs)
    {
        foreach (var header in dirs.OfType<QuickTimeMovieHeaderDirectory>())
            if (header.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var created)
                && created >= EarliestPlausibleDate)
                return created;
        return null;
    }

    private static void ReadCamera(MediaFile file, IReadOnlyList<MetaDirectory> dirs)
    {
        foreach (var ifd0 in dirs.OfType<ExifIfd0Directory>())
        {
            file.CameraMake ??= Clean(ifd0.GetDescription(ExifDirectoryBase.TagMake));
            file.CameraModel ??= Clean(ifd0.GetDescription(ExifDirectoryBase.TagModel));
        }
        // iPhone-style videos carry make/model in the QuickTime metadata header instead.
        foreach (var qt in dirs.OfType<QuickTimeMetadataHeaderDirectory>())
        {
            file.CameraMake ??= Clean(qt.GetDescription(QuickTimeMetadataHeaderDirectory.TagMake));
            file.CameraModel ??= Clean(qt.GetDescription(QuickTimeMetadataHeaderDirectory.TagModel));
        }
    }

    private static void ReadGps(MediaFile file, IReadOnlyList<MetaDirectory> dirs)
    {
        foreach (var gps in dirs.OfType<GpsDirectory>())
        {
            var location = gps.GetGeoLocation();
            if (location is null || location.IsZero) continue;
            file.GpsLatitude = location.Latitude;
            file.GpsLongitude = location.Longitude;
            return;
        }
    }

    private static void ReadDimensions(MediaFile file, IReadOnlyList<MetaDirectory> dirs)
    {
        int width, height;
        if (TryDims<JpegDirectory>(dirs, JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight, out width, out height)
            || TryDims<PngDirectory>(dirs, PngDirectory.TagImageWidth, PngDirectory.TagImageHeight, out width, out height)
            || TryDims<ExifSubIfdDirectory>(dirs, ExifDirectoryBase.TagExifImageWidth, ExifDirectoryBase.TagExifImageHeight, out width, out height)
            || TryDims<ExifIfd0Directory>(dirs, ExifDirectoryBase.TagImageWidth, ExifDirectoryBase.TagImageHeight, out width, out height)
            || TryDims<QuickTimeTrackHeaderDirectory>(dirs, QuickTimeTrackHeaderDirectory.TagWidth, QuickTimeTrackHeaderDirectory.TagHeight, out width, out height))
        {
            file.PixelWidth = width;
            file.PixelHeight = height;
        }
    }

    /// <summary>Best-effort extra fields for the rename tokens; anything absent simply stays null.</summary>
    private static void ReadExtended(MediaFile file, IReadOnlyList<MetaDirectory> dirs)
    {
        foreach (var sub in dirs.OfType<ExifSubIfdDirectory>())
        {
            if (file.FNumber is null && sub.TryGetDouble(ExifDirectoryBase.TagFNumber, out var fNumber))
                file.FNumber = fNumber;
            if (file.IsoSpeed is null && sub.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                file.IsoSpeed = iso;
            if (file.FocalLengthMm is null && sub.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var focal))
                file.FocalLengthMm = focal;
            file.ExposureTime ??= CleanExposure(sub.GetDescription(ExifDirectoryBase.TagExposureTime));
            file.LensModel ??= Clean(sub.GetDescription(ExifDirectoryBase.TagLensModel));
        }
        foreach (var ifd0 in dirs.OfType<ExifIfd0Directory>())
        {
            file.Artist ??= Clean(ifd0.GetDescription(ExifDirectoryBase.TagArtist));
            file.Software ??= Clean(ifd0.GetDescription(ExifDirectoryBase.TagSoftware));
            if (file.Orientation is null && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation))
                file.Orientation = orientation;
        }
        // Some tools only record the lens in XMP (aux:Lens and friends).
        if (file.LensModel is null)
            foreach (var xmp in dirs.OfType<XmpDirectory>())
            {
                foreach (var (key, value) in xmp.GetXmpProperties())
                    if (key.EndsWith(":Lens", StringComparison.OrdinalIgnoreCase)
                        || key.EndsWith(":LensModel", StringComparison.OrdinalIgnoreCase))
                    {
                        file.LensModel = Clean(value);
                        break;
                    }
                if (file.LensModel is not null) break;
            }
        foreach (var header in dirs.OfType<QuickTimeMovieHeaderDirectory>())
        {
            // MetadataExtractor 2.8 stores mvhd duration as a TimeSpan (already timescale-corrected).
            if (header.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration
                && duration > TimeSpan.Zero)
            {
                file.DurationSeconds = duration.TotalSeconds;
                break;
            }
        }
    }

    /// <summary>"1/250 sec" (the library's rendering) → the model's "1/250" convention.</summary>
    private static string? CleanExposure(string? description)
    {
        var cleaned = Clean(description);
        if (cleaned is null) return null;
        if (cleaned.EndsWith(" sec", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[..^4].TrimEnd();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static bool TryDims<T>(IReadOnlyList<MetaDirectory> dirs, int widthTag, int heightTag, out int width, out int height)
        where T : MetaDirectory
    {
        foreach (var dir in dirs.OfType<T>())
            if (dir.TryGetInt32(widthTag, out width) && dir.TryGetInt32(heightTag, out height)
                && width > 0 && height > 0)
                return true;
        width = height = 0;
        return false;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
