using Photon.Core.Models;

namespace Photon.Tests;

public class FileScannerTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly Photon.Core.Services.FileScanner _scanner = TestServices.FileScanner();

    public FileScannerTests()
    {
        _t.File("root/a.jpg", 1000);
        _t.File("root/b.mp4", 2000);
        _t.TextFile("root/c.txt", "not media");
        _t.File("root/sub/d.jpg", 3000);
    }

    public void Dispose() => _t.Dispose();

    private static ScanFilter Filter(bool recursive = true,
        string[]? pictures = null, string[]? videos = null) => new()
    {
        PictureExtensions = [.. pictures ?? [".jpg"]],
        VideoExtensions = [.. videos ?? []],
        Recursive = recursive,
    };

    [Fact]
    public async Task Recursive_FindsOnlyMatchingExtensions()
    {
        var files = await _scanner.ScanAsync(_t.At("root"), Filter());

        Assert.Equal(new HashSet<string> { _t.At("root/a.jpg"), _t.At("root/sub/d.jpg") },
            files.Select(f => f.FilePath).ToHashSet());
        Assert.All(files, f => Assert.False(f.IsVideo));
    }

    [Fact]
    public async Task NonRecursive_TopLevelOnly()
    {
        var files = await _scanner.ScanAsync(_t.At("root"), Filter(recursive: false));

        Assert.Equal(_t.At("root/a.jpg"), Assert.Single(files).FilePath);
    }

    [Fact]
    public async Task VideoExtensions_FlagIsVideo()
    {
        var files = await _scanner.ScanAsync(_t.At("root"), Filter(videos: [".mp4"]));

        var video = files.Single(f => f.Extension == ".mp4");
        Assert.True(video.IsVideo);
        Assert.Equal(2000, video.SizeBytes);
        Assert.False(files.Single(f => f.FilePath == _t.At("root/a.jpg")).IsVideo);
    }

    [Fact]
    public async Task MediaFileFields_PopulatedFromDisk()
    {
        var stamp = new DateTime(2021, 4, 3, 2, 1, 0);
        File.SetLastWriteTime(_t.At("root/a.jpg"), stamp);

        var files = await _scanner.ScanAsync(_t.At("root"), Filter(recursive: false));

        var f = Assert.Single(files);
        Assert.Equal(1000, f.SizeBytes);
        AssertEx.SameSecond(stamp, f.FileModified);
        Assert.False(f.MetadataLoaded);   // EXIF is IMetadataReader's job, not the scanner's
        Assert.Null(f.ExifDate);
    }

    [Fact]
    public async Task Progress_ReportsFilesFound()
    {
        int last = 0;
        var progress = new SyncProgress<int>(n => last = n);

        var files = await _scanner.ScanAsync(_t.At("root"), Filter(), progress);

        Assert.Equal(files.Count, last);
    }

    [Fact]
    public async Task EmptyFolder_ReturnsEmptyList()
    {
        var empty = _t.Dir("empty");
        Assert.Empty(await _scanner.ScanAsync(empty, Filter()));
    }
}
