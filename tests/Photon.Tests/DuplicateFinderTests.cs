using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

public class DuplicateFinderTests : IDisposable
{
    // Larger than 2 x 64 KB so the quick hash's first/last windows don't overlap.
    private const int BigFile = 200_000;

    private readonly TempDir _t = new();
    private readonly JournalService _journal;
    private readonly DuplicateFinder _finder;

    public DuplicateFinderTests()
    {
        _journal = TestServices.Journal(_t.Dir("journals"));
        _finder = TestServices.DuplicateFinder(_journal);
    }

    public void Dispose() => _t.Dispose();

    private DuplicateFinderOptions Options(DuplicateCompareMode mode, params string[] folders) => new()
    {
        Folders = [.. folders.Select(f => _t.At(f))],
        CompareMode = mode,
    };

    [Theory]
    [InlineData(DuplicateCompareMode.QuickHash)]
    [InlineData(DuplicateCompareMode.FullHash)]
    public async Task IdenticalFiles_GroupTogether(DuplicateCompareMode mode)
    {
        var content = _t.Bytes(BigFile);
        var a = _t.File("pics/a.jpg", content);
        var b = _t.File("pics/sub/b.jpg", content);
        var c = _t.File("pics/c.jpg", content);
        _t.File("pics/unrelated.jpg", BigFile);   // different content, same size

        var result = await _finder.ScanAsync(Options(mode, "pics"));

        var group = Assert.Single(result.Groups);
        Assert.Equal(new HashSet<string> { a, b, c }, group.Files.ToHashSet());
        Assert.Equal(BigFile, group.FileSizeBytes);
        Assert.True(result.FilesScanned >= 4);
    }

    [Fact]
    public async Task QuickHash_SameSizeDifferentTail_NotGrouped()
    {
        var content = _t.Bytes(BigFile);
        _t.File("pics/a.jpg", content);
        var tailDiffers = (byte[])content.Clone();
        for (int i = BigFile - 10; i < BigFile; i++) tailDiffers[i] ^= 0xFF;
        _t.File("pics/b.jpg", tailDiffers);

        var result = await _finder.ScanAsync(Options(DuplicateCompareMode.QuickHash, "pics"));

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task NameAndSize_MatchesByNameAndSizeOnly()
    {
        var size = 5000;
        var x1 = _t.File("d1/x.jpg", _t.Bytes(size));
        var x2 = _t.File("d2/x.jpg", _t.Bytes(size));   // same name+size, different content
        _t.File("d1/y.jpg", _t.Bytes(size));            // same size, different name

        var result = await _finder.ScanAsync(Options(DuplicateCompareMode.NameAndSize, "d1", "d2"));

        var group = Assert.Single(result.Groups);
        Assert.Equal(new HashSet<string> { x1, x2 }, group.Files.ToHashSet());
    }

    [Fact]
    public async Task MinFileSize_FiltersSmallFiles()
    {
        var small = _t.Bytes(500);
        _t.File("pics/s1.jpg", small);
        _t.File("pics/s2.jpg", small);
        var big = _t.Bytes(2000);
        var b1 = _t.File("pics/b1.jpg", big);
        var b2 = _t.File("pics/b2.jpg", big);

        var o = Options(DuplicateCompareMode.FullHash, "pics");
        o.MinFileSizeBytes = 1000;

        var result = await _finder.ScanAsync(o);

        var group = Assert.Single(result.Groups);
        Assert.Equal(new HashSet<string> { b1, b2 }, group.Files.ToHashSet());
    }

    [Fact]
    public void WastedBytes_Math()
    {
        var g = new DuplicateGroup { Key = "k", FileSizeBytes = 1000, Files = { "/a", "/b", "/c" } };
        Assert.Equal(2000, g.WastedBytes);

        var single = new DuplicateGroup { Key = "k2", FileSizeBytes = 1000, Files = { "/a" } };
        Assert.Equal(0, single.WastedBytes);

        var scan = new DuplicateScanResult { Groups = { g, single } };
        Assert.Equal(2000, scan.TotalWastedBytes);
    }

    [Fact]
    public async Task ScanResult_WastedBytes_FromRealFiles()
    {
        var content = _t.Bytes(3000);
        _t.File("pics/a.jpg", content);
        _t.File("pics/b.jpg", content);
        _t.File("pics/c.jpg", content);

        var result = await _finder.ScanAsync(Options(DuplicateCompareMode.FullHash, "pics"));

        Assert.Equal(6000, result.TotalWastedBytes);   // 2 redundant copies x 3000 bytes
    }

    // ---------- keeper policies (observed through MoveToFolder resolution) ----------

    private async Task<(string Kept, HashSet<string> Moved)> ResolveWithPolicy(
        DuplicateKeepPolicy policy, params string[] files)
    {
        var o = Options(DuplicateCompareMode.FullHash, "pics");
        o.Resolution = DuplicateResolution.MoveToFolder;
        o.KeepPolicy = policy;
        o.MoveToFolder = _t.Dir("dupes");

        var scan = await _finder.ScanAsync(o);
        Assert.Single(scan.Groups);
        var result = await _finder.ResolveAsync(scan, o);
        Assert.Empty(result.Errors);

        var kept = files.Single(File.Exists);
        var movedNames = Directory.GetFiles(_t.At("dupes"), "*", SearchOption.AllDirectories)
            .Select(p => Path.GetFileName(p))
            .ToHashSet();
        return (kept, movedNames);
    }

    [Fact]
    public async Task KeepPolicy_Oldest()
    {
        var content = _t.Bytes(4000);
        var a = _t.File("pics/a.jpg", content);
        var b = _t.File("pics/b.jpg", content);
        var c = _t.File("pics/c.jpg", content);
        File.SetLastWriteTime(a, new DateTime(2020, 1, 1));
        File.SetLastWriteTime(b, new DateTime(2021, 1, 1));
        File.SetLastWriteTime(c, new DateTime(2022, 1, 1));

        var (kept, moved) = await ResolveWithPolicy(DuplicateKeepPolicy.Oldest, a, b, c);

        Assert.Equal(a, kept);
        Assert.Equal(new HashSet<string> { "b.jpg", "c.jpg" }, moved);
    }

    [Fact]
    public async Task KeepPolicy_Newest()
    {
        var content = _t.Bytes(4000);
        var a = _t.File("pics/a.jpg", content);
        var b = _t.File("pics/b.jpg", content);
        File.SetLastWriteTime(a, new DateTime(2020, 1, 1));
        File.SetLastWriteTime(b, new DateTime(2022, 1, 1));

        var (kept, _) = await ResolveWithPolicy(DuplicateKeepPolicy.Newest, a, b);

        Assert.Equal(b, kept);
    }

    [Fact]
    public async Task KeepPolicy_ShortestPath()
    {
        var content = _t.Bytes(4000);
        var shallow = _t.File("pics/a.jpg", content);
        var deep = _t.File("pics/deeply/nested/folder/copy_of_a.jpg", content);
        File.SetLastWriteTime(shallow, new DateTime(2022, 1, 1));   // newer, so date can't be the tiebreak
        File.SetLastWriteTime(deep, new DateTime(2020, 1, 1));

        var (kept, _) = await ResolveWithPolicy(DuplicateKeepPolicy.ShortestPath, shallow, deep);

        Assert.Equal(shallow, kept);
    }

    [Fact]
    public async Task KeepPolicy_FirstAlphabetical()
    {
        var content = _t.Bytes(4000);
        var a = _t.File("pics/alpha.jpg", content);
        var z = _t.File("pics/zulu.jpg", content);
        File.SetLastWriteTime(a, new DateTime(2022, 1, 1));
        File.SetLastWriteTime(z, new DateTime(2020, 1, 1));

        var (kept, _) = await ResolveWithPolicy(DuplicateKeepPolicy.FirstAlphabetical, a, z);

        Assert.Equal(a, kept);
    }

    [Fact]
    public async Task ResolveAsync_NestedMoveFolder_UndoPrunesWholeCreatedChain()
    {
        var content = _t.Bytes(4000);
        var a = _t.File("pics/a.jpg", content);
        var b = _t.File("pics/b.jpg", content);
        File.SetLastWriteTime(a, new DateTime(2020, 1, 1));
        File.SetLastWriteTime(b, new DateTime(2021, 1, 1));

        var o = Options(DuplicateCompareMode.FullHash, "pics");
        o.Resolution = DuplicateResolution.MoveToFolder;
        o.KeepPolicy = DuplicateKeepPolicy.Oldest;
        o.MoveToFolder = _t.At("review/dups");   // neither level exists yet

        var scan = await _finder.ScanAsync(o);
        var result = await _finder.ResolveAsync(scan, o);
        Assert.Empty(result.Errors);
        Assert.True(Directory.Exists(_t.At("review/dups")));

        var journal = _journal.LoadLatestUndoable();
        Assert.NotNull(journal);
        // Every created level is journaled (deepest first), not just the leaf.
        Assert.Contains(_t.At("review/dups"), journal!.CreatedDirectories);
        Assert.Contains(_t.At("review"), journal.CreatedDirectories);

        var undo = await _journal.UndoAsync(journal);

        Assert.Empty(undo.Errors);
        Assert.True(File.Exists(b));                          // duplicate back home
        Assert.False(Directory.Exists(_t.At("review")));      // whole created chain pruned
    }

    [Fact]
    public void PickKeeper_PublicAndDeterministic()
    {
        // Public so the Duplicate Finder UI marks exactly the file the engine keeps.
        var files = new List<string> { _t.At("pics/b.jpg"), _t.At("pics/a.jpg") };
        Assert.Equal(_t.At("pics/a.jpg"),
            DuplicateFinder.PickKeeper(files, DuplicateKeepPolicy.FirstAlphabetical));
    }

    [Fact]
    public async Task ResolveAsync_MoveToFolder_MovesAllButKeeper_AndJournals()
    {
        var content = _t.Bytes(4000);
        var a = _t.File("pics/a.jpg", content);
        var b = _t.File("pics/b.jpg", content);
        File.SetLastWriteTime(a, new DateTime(2020, 1, 1));
        File.SetLastWriteTime(b, new DateTime(2021, 1, 1));

        var o = Options(DuplicateCompareMode.FullHash, "pics");
        o.Resolution = DuplicateResolution.MoveToFolder;
        o.KeepPolicy = DuplicateKeepPolicy.Oldest;
        o.MoveToFolder = _t.Dir("dupes");

        var scan = await _finder.ScanAsync(o);
        var result = await _finder.ResolveAsync(scan, o);

        Assert.Equal(1, result.DuplicatesDiverted);   // one non-keeper relocated
        Assert.True(File.Exists(a));
        Assert.False(File.Exists(b));
        var moved = Assert.Single(Directory.GetFiles(_t.At("dupes"), "*", SearchOption.AllDirectories));
        Assert.Equal(content, File.ReadAllBytes(moved));

        // The move is journaled so History can undo it.
        Assert.NotNull(result.JournalPath);
        Assert.True(File.Exists(result.JournalPath));
        Assert.NotEmpty(_journal.LoadAll());
    }
}
