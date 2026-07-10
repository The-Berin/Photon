using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.QuickTime;
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
