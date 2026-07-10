namespace Photon.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly Photon.Core.Services.FolderScanner _scanner = TestServices.FolderScanner();

    public FolderScannerTests() => BuildTree();

    public void Dispose() => _t.Dispose();

    // root/
    //   a.jpg      10_000
    //   b.txt         500
    //   sub1/
    //     c.jpg    20_000
    //     d.mp4    50_000
    //     sub2/sub3/
    //       e.jpg   1_000
    //   empty/
    private void BuildTree()
    {
        var a = _t.File("root/a.jpg", 10_000);
        _t.File("root/b.txt", 500);
        _t.File("root/sub1/c.jpg", 20_000);
        _t.File("root/sub1/d.mp4", 50_000);
        var e = _t.File("root/sub1/sub2/sub3/e.jpg", 1_000);
        _t.Dir("root", "empty");

        File.SetLastWriteTime(a, new DateTime(2019, 1, 1, 8, 0, 0));
        File.SetLastWriteTime(e, new DateTime(2024, 6, 1, 8, 0, 0));
    }

    [Fact]
    public async Task Counts_And_Bytes()
    {
        var report = await _scanner.ScanAsync(_t.At("root"));

        Assert.Equal(_t.At("root"), report.Root);
        Assert.Equal(5, report.TotalFiles);
        Assert.Equal(81_500, report.TotalBytes);
        Assert.InRange(report.TotalFolders, 4, 5);   // sub1, sub2, sub3, empty (+root, if counted)

        Assert.Equal(3, report.PictureCount);
        Assert.Equal(31_000, report.PictureBytes);
        Assert.Equal(1, report.VideoCount);
        Assert.Equal(50_000, report.VideoBytes);
        Assert.Equal(1, report.OtherCount);
        Assert.Equal(500, report.OtherBytes);
    }

    [Fact]
    public async Task ByExtension_Breakdown()
    {
        var report = await _scanner.ScanAsync(_t.At("root"));

        Assert.Equal((3, 31_000L), report.ByExtension[".jpg"]);
        Assert.Equal((1, 50_000L), report.ByExtension[".mp4"]);
        Assert.Equal((1, 500L), report.ByExtension[".txt"]);
        Assert.Equal(3, report.ByExtension.Count);
    }

    [Fact]
    public async Task MaxDepth_OnNestedTree()
    {
        var report = await _scanner.ScanAsync(_t.At("root"));

        // sub1/sub2/sub3 is three levels below the root (4 if depth counts the file itself).
        Assert.InRange(report.MaxDepth, 3, 4);
    }

    [Fact]
    public async Task LargestFile_Identified()
    {
        var report = await _scanner.ScanAsync(_t.At("root"));

        Assert.Equal(_t.At("root/sub1/d.mp4"), report.LargestFilePath);
        Assert.Equal(50_000, report.LargestFileBytes);
    }

    [Fact]
    public async Task OldestAndNewestDates_Tracked()
    {
        var report = await _scanner.ScanAsync(_t.At("root"));

        Assert.NotNull(report.OldestFileDate);
        AssertEx.SameSecond(new DateTime(2019, 1, 1, 8, 0, 0), report.OldestFileDate!.Value);
        Assert.NotNull(report.NewestFileDate);
        Assert.True(report.NewestFileDate >= new DateTime(2024, 6, 1, 8, 0, 0));
    }

    [Fact]
    public async Task CleanTree_NoInaccessibleItems()
    {
        var report = await _scanner.ScanAsync(_t.At("root"));

        Assert.Equal(0, report.InaccessibleItems);
    }

    [Fact]
    public async Task EmptyFolder_ZeroEverything()
    {
        var empty = _t.Dir("nothing");
        var report = await _scanner.ScanAsync(empty);

        Assert.Equal(0, report.TotalFiles);
        Assert.Equal(0, report.TotalBytes);
        Assert.Equal(0, report.InaccessibleItems);
        Assert.Null(report.LargestFilePath);
    }

    [Fact]
    public void EstimateSortTime_Math()
    {
        var report = new Photon.Core.Models.FolderScanReport { Root = "/x", PictureBytes = 4000, VideoBytes = 6000 };
        Assert.Equal(TimeSpan.FromSeconds(10), report.EstimateSortTime(1000));
        Assert.Equal(TimeSpan.Zero, report.EstimateSortTime(0));
    }
}
