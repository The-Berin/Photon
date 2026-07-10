using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

public class FolderFlattenerTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly JournalService _journal;
    private readonly FolderFlattener _flattener;

    public FolderFlattenerTests()
    {
        _journal = TestServices.Journal(_t.Dir("journals"));
        _flattener = TestServices.Flattener(_journal);
    }

    public void Dispose() => _t.Dispose();

    private async Task<FlattenResult> Flatten(FlattenOptions options)
    {
        var plan = await _flattener.BuildPlanAsync(options);
        return await _flattener.ExecuteAsync(plan, options);
    }

    private string[] RootFiles(string root) =>
        Directory.GetFiles(_t.At(root))
            .Select(p => Path.GetFileName(p))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public async Task NestedTree_FlattensToRoot()
    {
        var root = _t.Dir("flat");
        _t.File("flat/top.jpg", 100);
        _t.File("flat/s1/one.jpg", 100);
        _t.File("flat/s1/s2/two.jpg", 100);

        var result = await Flatten(new FlattenOptions { Root = root });

        Assert.Equal(2, result.Moved);           // top.jpg was already at the root
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "one.jpg", "top.jpg", "two.jpg" }, RootFiles("flat"));
    }

    [Fact]
    public async Task Conflict_AppendNumber()
    {
        var root = _t.Dir("flat");
        var rootContent = _t.Bytes(100);
        var nestedContent = _t.Bytes(100);
        _t.File("flat/x.jpg", rootContent);
        _t.File("flat/s1/x.jpg", nestedContent);

        var result = await Flatten(new FlattenOptions { Root = root, ConflictPolicy = FlattenConflictPolicy.AppendNumber });

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "x.jpg", "x_1.jpg" }, RootFiles("flat"));
        Assert.Equal(rootContent, File.ReadAllBytes(_t.At("flat/x.jpg")));      // resident file untouched
        Assert.Equal(nestedContent, File.ReadAllBytes(_t.At("flat/x_1.jpg")));
    }

    [Fact]
    public async Task Conflict_AppendFolderName()
    {
        var root = _t.Dir("flat");
        var c1 = _t.Bytes(100);
        var c2 = _t.Bytes(100);
        _t.File("flat/s1/x.jpg", c1);
        _t.File("flat/s2/x.jpg", c2);

        var result = await Flatten(new FlattenOptions { Root = root, ConflictPolicy = FlattenConflictPolicy.AppendFolderName });

        Assert.Empty(result.Errors);
        var names = RootFiles("flat");
        Assert.Equal(2, names.Length);
        Assert.Contains(names, n => n.Contains("s1") || n.Contains("s2"));   // conflict resolved via folder name

        // No content lost, whichever file won the plain name.
        var contents = names.Select(n => File.ReadAllBytes(_t.At("flat/" + n))).ToArray();
        Assert.Contains(contents, b => b.SequenceEqual(c1));
        Assert.Contains(contents, b => b.SequenceEqual(c2));
    }

    [Fact]
    public async Task Conflict_Skip_LeavesConflictingFileInPlace()
    {
        var root = _t.Dir("flat");
        _t.File("flat/x.jpg", 100);
        var nested = _t.File("flat/s1/x.jpg", 100);

        var options = new FlattenOptions { Root = root, ConflictPolicy = FlattenConflictPolicy.Skip };
        var plan = await _flattener.BuildPlanAsync(options);
        var result = await _flattener.ExecuteAsync(plan, options);

        // The skip is surfaced either at plan time (warning) or at run time (Skipped count).
        Assert.True(result.Skipped >= 1 || plan.Warnings.Count > 0);
        Assert.True(File.Exists(nested));                    // skipped, not moved
        Assert.Equal(new[] { "x.jpg" }, RootFiles("flat"));
    }

    [Fact]
    public async Task EmptyDirs_RemovedOnlyWhenFlagSet()
    {
        var rootKeep = _t.Dir("keep");
        _t.File("keep/s1/one.jpg", 50);
        await Flatten(new FlattenOptions { Root = rootKeep, RemoveEmptyFolders = false });
        Assert.True(Directory.Exists(_t.At("keep/s1")));     // left behind, empty

        var rootPrune = _t.Dir("prune");
        _t.File("prune/s1/one.jpg", 50);
        var result = await Flatten(new FlattenOptions { Root = rootPrune, RemoveEmptyFolders = true });
        Assert.False(Directory.Exists(_t.At("prune/s1")));
        Assert.True(result.FoldersRemoved >= 1);
    }

    [Fact]
    public async Task PreExistingEmptyFolders_SurviveFlatten()
    {
        // Folders the flatten did not empty are not ours to delete: undo could never
        // recreate them, so the pre-flatten structure would be unrestorable.
        var root = _t.Dir("flat");
        _t.File("flat/s1/one.jpg", 50);
        _t.Dir("flat/empty-album");           // already empty before the flatten
        _t.Dir("flat/a/b");                   // empty chain, no files anywhere

        var options = new FlattenOptions { Root = root, RemoveEmptyFolders = true };
        var plan = await _flattener.BuildPlanAsync(options);
        var result = await _flattener.ExecuteAsync(plan, options);

        Assert.Equal(1, plan.FoldersToRemove);               // only s1 is removable
        Assert.Equal(1, result.FoldersRemoved);
        Assert.False(Directory.Exists(_t.At("flat/s1")));    // emptied by the run: removed
        Assert.True(Directory.Exists(_t.At("flat/empty-album")));
        Assert.True(Directory.Exists(_t.At("flat/a/b")));
    }

    [Fact]
    public async Task MediaOnly_LeavesNonMediaInPlace()
    {
        var root = _t.Dir("flat");
        var pic = _t.File("flat/s1/pic.jpg", 100);
        var note = _t.TextFile("flat/s1/note.txt", "keep me here");

        await Flatten(new FlattenOptions { Root = root, MediaOnly = true, RemoveEmptyFolders = true });

        Assert.True(File.Exists(_t.At("flat/pic.jpg")));
        Assert.False(File.Exists(pic));
        Assert.True(File.Exists(note));                      // .txt stays put
        Assert.True(Directory.Exists(_t.At("flat/s1")));     // not empty, so not removed
    }

    [Fact]
    public async Task Undo_RestoresTheTree()
    {
        var root = _t.Dir("flat");
        var one = _t.Bytes(120);
        var two = _t.Bytes(130);
        _t.File("flat/s1/one.jpg", one);
        _t.File("flat/s1/s2/two.jpg", two);

        var result = await Flatten(new FlattenOptions { Root = root, RemoveEmptyFolders = true });
        Assert.NotNull(result.JournalPath);
        Assert.True(File.Exists(result.JournalPath));
        Assert.False(Directory.Exists(_t.At("flat/s1")));

        var journal = _journal.LoadLatestUndoable();
        Assert.NotNull(journal);
        var undo = await _journal.UndoAsync(journal!);

        Assert.Empty(undo.Errors);
        Assert.Equal(one, File.ReadAllBytes(_t.At("flat/s1/one.jpg")));
        Assert.Equal(two, File.ReadAllBytes(_t.At("flat/s1/s2/two.jpg")));
        Assert.False(File.Exists(_t.At("flat/one.jpg")));
        Assert.False(File.Exists(_t.At("flat/two.jpg")));
    }
}
