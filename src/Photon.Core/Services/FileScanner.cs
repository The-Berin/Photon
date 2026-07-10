using Photon.Core.Models;

namespace Photon.Core.Services;

/// <summary>Default <see cref="IFileScanner"/>: filtered enumeration that survives per-file/per-directory access errors.</summary>
public sealed class FileScanner : IFileScanner
{
    private const int ReportEvery = 100;

    public Task<List<MediaFile>> ScanAsync(string folder, ScanFilter filter,
        IProgress<int>? filesFound = null, CancellationToken ct = default)
        => Task.Run(() => Scan(folder, filter, filesFound, ct), ct);

    private static List<MediaFile> Scan(string folder, ScanFilter filter, IProgress<int>? filesFound, CancellationToken ct)
    {
        var results = new List<MediaFile>();
        var enumeration = new EnumerationOptions
        {
            // IgnoreInaccessible keeps the walk alive across AccessDenied directories.
            IgnoreInaccessible = true,
            RecurseSubdirectories = filter.Recursive,
        };

        foreach (var path in Directory.EnumerateFiles(folder, "*", enumeration))
        {
            ct.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var isVideo = filter.VideoExtensions.Contains(ext);
            if (!isVideo && !filter.PictureExtensions.Contains(ext)) continue;

            try
            {
                var info = new FileInfo(path);
                results.Add(new MediaFile
                {
                    FilePath = path,
                    SizeBytes = info.Length,
                    FileCreated = info.CreationTime,
                    FileModified = info.LastWriteTime,
                    IsVideo = isVideo,
                });
            }
            catch
            {
                // A file deleted/locked mid-scan must never abort the whole scan.
                continue;
            }

            if (results.Count % ReportEvery == 0) filesFound?.Report(results.Count);
        }

        filesFound?.Report(results.Count);
        return results;
    }
}
