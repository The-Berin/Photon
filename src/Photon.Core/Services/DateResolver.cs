using Photon.Core.Models;

namespace Photon.Core.Services;

/// <summary>Default <see cref="IDateResolver"/>: pure application of a <see cref="DateSource"/> policy.</summary>
public sealed class DateResolver : IDateResolver
{
    public DateResolution Resolve(MediaFile file, DateSource source)
    {
        DateTime? fileDate = BestFileDate(file);
        DateTime? exif = file.ExifDate;

        return source switch
        {
            DateSource.ExifThenFileDate => exif is not null
                ? new DateResolution(exif, true)
                : new DateResolution(fileDate, false),
            DateSource.ExifOnly => exif is not null
                ? new DateResolution(exif, true)
                : new DateResolution(null, false),
            DateSource.FileDateThenExif => fileDate is not null
                ? new DateResolution(fileDate, false)
                : exif is not null ? new DateResolution(exif, true) : new DateResolution(null, false),
            DateSource.FileDateOnly => new DateResolution(fileDate, false),
            _ => new DateResolution(null, false),
        };
    }

    // Cameras and copy tools often reset one of the two stamps; the earlier one is the best guess.
    private static DateTime? BestFileDate(MediaFile file)
    {
        var created = file.FileCreated;
        var modified = file.FileModified;
        if (created == default && modified == default) return null;
        if (created == default) return modified;
        if (modified == default) return created;
        return created <= modified ? created : modified;
    }
}
