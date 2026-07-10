using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

public class SortExecutorTests : InvariantCultureTest, IDisposable
{
    private readonly TempDir _t = new();
    private readonly JournalService _journal;
    private readonly SortExecutor _executor;
    private readonly string _src;
    private readonly string _dest;

    public SortExecutorTests()
    {
        _journal = TestServices.Journal(_t.Dir("journals"));
        _executor = TestServices.Executor(_journal);
        _src = _t.Dir("src");
        _dest = _t.Dir("dest");
    }

    public void Dispose() => _t.Dispose();

    private SortOptions Options(SortAction action = SortAction.Copy,
        DuplicateHandling duplicates = DuplicateHandling.Rename) => new()
    {
        SourceFolder = _src,
        OutputFolder = _dest,
        Action = action,
        DuplicateHandling = duplicates,
    };

    private static SortPlan Plan(string destRoot, params (MediaFile Src, string Dest)[] items) => new()
    {
        DestinationRoot = destRoot,
        Items = [.. items.Select(x => new SortPlanItem
        {
            Source = x.Src,
            PlannedDestination = x.Dest,
            ResolvedDate = Media.DefaultStamp,
            DateFromExif = true,
        })],
        TotalBytes = items.Sum(x => x.Src.SizeBytes),
        RequiredBytes = items.Sum(x => x.Src.SizeBytes),
        DestinationFreeBytes = long.MaxValue,
    };

    private MediaFile SourceFile(string name, int size = 4096, string? make = null, string? model = null)
    {
        var path = _t.File("src/" + name, size);
        return Media.File(path, size, exif: Media.DefaultStamp, make: make, model: model);
    }

    [Fact]
    public async Task CopyRun_IdenticalContent_PreservedTimestamp_JournalEntries()
    {
        var stamp = new DateTime(2020, 5, 6, 12, 34, 56);
        var a = SourceFile("a.jpg");
        var b = SourceFile("b.jpg");
        File.SetLastWriteTime(a.FilePath, stamp);
        File.SetLastWriteTime(b.FilePath, stamp);

        var destA = _t.At("dest/2023/March/a.jpg");
        var destB = _t.At("dest/2023/March/b.jpg");
        var result = await _executor.ExecuteAsync(Plan(_dest, (a, destA), (b, destB)), Options());

        Assert.Equal(2, result.Copied);
        Assert.Empty(result.Errors);
        Assert.False(result.Cancelled);

        // Sources untouched, destinations byte-identical, mtime preserved to the second.
        foreach (var (src, dst) in new[] { (a.FilePath, destA), (b.FilePath, destB) })
        {
            Assert.True(File.Exists(src));
            Assert.True(File.Exists(dst));
            Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(dst));
            AssertEx.SameSecond(stamp, File.GetLastWriteTime(dst));
        }

        Assert.NotNull(result.JournalPath);
        Assert.True(File.Exists(result.JournalPath));
        var journal = Assert.Single(_journal.LoadAll());
        Assert.Equal(2, journal.Entries.Count);
        Assert.All(journal.Entries, e => Assert.Equal(JournalOperation.Copied, e.Operation));
        Assert.Equal(new[] { a.FilePath, b.FilePath }.ToHashSet(),
            journal.Entries.Select(e => e.OriginalPath).ToHashSet());
        Assert.Equal(new[] { destA, destB }.ToHashSet(),
            journal.Entries.Select(e => e.NewPath!).ToHashSet());
        // Recorded so undo can detect post-sort edits and stash instead of hard-deleting.
        Assert.All(journal.Entries, e => Assert.NotNull(e.NewFileWriteTimeUtc));
    }

    [Fact]
    public async Task MoveRun_RelocatesFiles()
    {
        var a = SourceFile("a.jpg");
        var originalContent = File.ReadAllBytes(a.FilePath);
        var destA = _t.At("dest/2023/a.jpg");

        var result = await _executor.ExecuteAsync(Plan(_dest, (a, destA)), Options(SortAction.Move));

        Assert.Equal(1, result.Moved);
        Assert.False(File.Exists(a.FilePath));
        Assert.True(File.Exists(destA));
        Assert.Equal(originalContent, File.ReadAllBytes(destA));

        var journal = Assert.Single(_journal.LoadAll());
        var entry = Assert.Single(journal.Entries);
        Assert.Equal(JournalOperation.Moved, entry.Operation);
        Assert.Equal(a.FilePath, entry.OriginalPath);
        Assert.Equal(destA, entry.NewPath);
    }

    [Fact]
    public async Task Collision_Rename_AppendsUnderscoreOne()
    {
        var a = SourceFile("f.jpg");
        var sourceContent = File.ReadAllBytes(a.FilePath);
        var occupant = _t.File("dest/2023/f.jpg", 128);
        var occupantContent = File.ReadAllBytes(occupant);

        var result = await _executor.ExecuteAsync(
            Plan(_dest, (a, _t.At("dest/2023/f.jpg"))), Options(duplicates: DuplicateHandling.Rename));

        Assert.Equal(1, result.RenamedOnCollision);
        Assert.Equal(occupantContent, File.ReadAllBytes(occupant));          // occupant untouched
        var renamed = _t.At("dest/2023/f_1.jpg");
        Assert.True(File.Exists(renamed));
        Assert.Equal(sourceContent, File.ReadAllBytes(renamed));

        var entry = Assert.Single(Assert.Single(_journal.LoadAll()).Entries);
        Assert.Equal(renamed, entry.NewPath);
    }

    [Fact]
    public async Task Collision_Skip_RecordedAndSourceUntouched()
    {
        var a = SourceFile("f.jpg");
        var occupant = _t.File("dest/2023/f.jpg", 128);
        var occupantContent = File.ReadAllBytes(occupant);

        var result = await _executor.ExecuteAsync(
            Plan(_dest, (a, _t.At("dest/2023/f.jpg"))), Options(duplicates: DuplicateHandling.Skip));

        Assert.Equal(1, result.SkippedExisting);
        Assert.True(File.Exists(a.FilePath));                                // source left unprocessed
        Assert.Equal(occupantContent, File.ReadAllBytes(occupant));          // destination unchanged
        Assert.False(File.Exists(_t.At("dest/2023/f_1.jpg")));

        var journal = Assert.Single(_journal.LoadAll());
        var entry = Assert.Single(journal.Entries);
        Assert.Equal(JournalOperation.SkippedExisting, entry.Operation);
        Assert.Equal(a.FilePath, entry.OriginalPath);
    }

    [Fact]
    public async Task Collision_Overwrite_StashesDisplacedFileInBackup()
    {
        var a = SourceFile("f.jpg");
        var sourceContent = File.ReadAllBytes(a.FilePath);
        var destPath = _t.At("dest/2023/f.jpg");
        _t.File("dest/2023/f.jpg", 128);
        var displacedContent = File.ReadAllBytes(destPath);

        var result = await _executor.ExecuteAsync(
            Plan(_dest, (a, destPath)), Options(duplicates: DuplicateHandling.Overwrite));

        Assert.Equal(1, result.Overwrote);
        Assert.Equal(sourceContent, File.ReadAllBytes(destPath));            // new content in place

        var journal = Assert.Single(_journal.LoadAll());
        var entry = Assert.Single(journal.Entries);
        Assert.Equal(JournalOperation.Overwrote, entry.Operation);
        Assert.NotNull(entry.DisplacedBackupPath);
        Assert.True(File.Exists(entry.DisplacedBackupPath));
        Assert.Equal(displacedContent, File.ReadAllBytes(entry.DisplacedBackupPath!));
    }

    [Fact]
    public async Task Overwrite_TransferFails_DisplacedDestinationFileRestored()
    {
        // The source vanished between scan and execute: the displaced destination file was
        // already stashed in the backup folder, and with no journal entry written it would
        // vanish unrecoverably — the failed transfer must roll the stash back.
        var destPath = _t.At("dest/2023/f.jpg");
        _t.File("dest/2023/f.jpg", 128);
        var displacedContent = File.ReadAllBytes(destPath);
        var ghost = Media.File(_t.At("src/ghost.jpg"), 100, exif: Media.DefaultStamp); // never on disk

        var result = await _executor.ExecuteAsync(
            Plan(_dest, (ghost, destPath)), Options(duplicates: DuplicateHandling.Overwrite));

        Assert.Single(result.Errors);
        Assert.Equal(0, result.Overwrote);
        Assert.True(File.Exists(destPath));                                  // back where it was
        Assert.Equal(displacedContent, File.ReadAllBytes(destPath));

        var journal = Assert.Single(_journal.LoadAll());
        Assert.Empty(journal.Entries);
        // Nothing left stranded in the backup folder.
        if (journal.BackupFolder is not null && Directory.Exists(journal.BackupFolder))
            Assert.Empty(Directory.GetFiles(journal.BackupFolder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Journal_PersistedBeforeFirstTransfer()
    {
        // A process kill mid-run must find the journal (and its backup folder) on disk.
        var a = SourceFile("a.jpg", 8192);
        var journalSeenOnFirstReport = false;
        var reported = false;
        var progress = new SyncProgress<SortProgress>(_ =>
        {
            if (reported) return;
            reported = true;
            journalSeenOnFirstReport = Directory.GetFiles(_journal.JournalDirectory, "*.json").Length > 0;
        });

        await _executor.ExecuteAsync(Plan(_dest, (a, _t.At("dest/2023/a.jpg"))), Options(), progress);

        Assert.True(reported);
        Assert.True(journalSeenOnFirstReport);
    }

    [Fact]
    public async Task DetectExactDuplicates_SubfolderOptionOff_SortsNormally()
    {
        var content = _t.Bytes(4096);
        var a = Media.File(_t.File("src/dup1.jpg", content), content.Length, exif: Media.DefaultStamp);
        var b = Media.File(_t.File("src/dup2.jpg", content), content.Length, exif: Media.DefaultStamp);

        var plan = Plan(_dest, (a, _t.At("dest/2023/dup1.jpg")), (b, _t.At("dest/2023/dup2.jpg")));
        plan.Items[1].IsContentDuplicate = true;

        var o = Options();
        o.DetectExactDuplicates = true;
        o.MoveDuplicatesToSubfolder = false;   // checkbox off: no diversion

        var result = await _executor.ExecuteAsync(plan, o);

        Assert.Equal(0, result.DuplicatesDiverted);
        Assert.Equal(2, result.Copied);
        Assert.True(File.Exists(_t.At("dest/2023/dup1.jpg")));
        Assert.True(File.Exists(_t.At("dest/2023/dup2.jpg")));               // at the planned spot
        Assert.False(Directory.Exists(_t.At("dest/Duplicates")));
    }

    [Fact]
    public async Task DetectExactDuplicates_DivertsIdenticalContent()
    {
        var content = _t.Bytes(4096);
        var a = Media.File(_t.File("src/dup1.jpg", content), content.Length, exif: Media.DefaultStamp);
        var b = Media.File(_t.File("src/dup2.jpg", content), content.Length, exif: Media.DefaultStamp);

        var plan = Plan(_dest, (a, _t.At("dest/2023/dup1.jpg")), (b, _t.At("dest/2023/dup2.jpg")));
        plan.Items[1].IsContentDuplicate = true;

        var o = Options();
        o.DetectExactDuplicates = true;
        o.MoveDuplicatesToSubfolder = true;

        var result = await _executor.ExecuteAsync(plan, o);

        Assert.Equal(1, result.DuplicatesDiverted);
        Assert.True(File.Exists(_t.At("dest/2023/dup1.jpg")));
        Assert.False(File.Exists(_t.At("dest/2023/dup2.jpg")));              // not at the planned spot

        // The diverted copy lives under a "Duplicates" folder inside the output root.
        var diverted = Directory.GetFiles(_dest, "dup2*", SearchOption.AllDirectories);
        Assert.All(diverted, p =>
            Assert.Contains("Duplicates", Path.GetRelativePath(_dest, p).Split(Path.DirectorySeparatorChar)));
    }

    [Fact]
    public async Task Cancellation_NoPartialFiles_JournalCoversCompletedItems()
    {
        var items = Enumerable.Range(0, 12)
            .Select(i =>
            {
                var f = SourceFile($"c{i:D2}.jpg", 8192);
                return (f, _t.At($"dest/2023/c{i:D2}.jpg"));
            })
            .ToArray();

        // Cancel on the first progress report — deterministically mid-run, regardless of
        // how the executor throttles its reports.
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<SortProgress>(_ => cts.Cancel());

        var result = await _executor.ExecuteAsync(Plan(_dest, items), Options(), progress, cts.Token);

        Assert.True(result.Cancelled);
        Assert.True(result.Processed < items.Length);

        // Clean cancellation: no half-written temp files anywhere under the test root.
        Assert.Empty(Directory.GetFiles(_t.Root, "*.photon-partial", SearchOption.AllDirectories));

        // The journal accounts for exactly the files that made it to the destination.
        Assert.NotNull(result.JournalPath);
        var journal = Assert.Single(_journal.LoadAll());
        var journaledDests = journal.Entries
            .Where(e => e.NewPath is not null)
            .Select(e => e.NewPath!)
            .ToHashSet();
        var actualDests = Directory.GetFiles(_dest, "*.jpg", SearchOption.AllDirectories).ToHashSet();
        Assert.Equal(actualDests, journaledDests);
        Assert.NotEmpty(actualDests);
    }

    [Fact]
    public async Task LogAndCsv_Created_HeaderExact_FieldsQuoted()
    {
        // A comma in the file name (legal on every OS, unlike '"') plus a quote in the
        // camera make exercises CSV quoting and quote-escaping end to end.
        var weird = SourceFile("we,ird name.jpg", make: "Ac\"me", model: "X100");
        var plain = SourceFile("plain.jpg");
        var weirdDest = _t.At("dest/2023/we,ird name.jpg");

        var o = Options();
        o.WriteLogFile = true;
        o.ExportCsvSummary = true;

        var result = await _executor.ExecuteAsync(
            Plan(_dest, (weird, weirdDest), (plain, _t.At("dest/2023/plain.jpg"))), o);

        Assert.NotNull(result.LogFilePath);
        Assert.True(File.Exists(result.LogFilePath));
        Assert.True(new FileInfo(result.LogFilePath!).Length > 0);

        Assert.NotNull(result.CsvPath);
        var lines = File.ReadAllLines(result.CsvPath!).Where(l => l.Length > 0).ToArray();
        Assert.Equal("original_path,new_path,date,camera,gps", lines[0]);
        Assert.Equal(3, lines.Length); // header + one row per processed file

        var rows = lines.Skip(1).Select(MiniCsv.ParseLine).ToList();
        Assert.All(rows, r => Assert.Equal(5, r.Count));

        var weirdRow = rows.Single(r => r[0] == weird.FilePath);   // round-trips only if properly quoted
        Assert.Equal(weirdDest, weirdRow[1]);
        Assert.Contains("Ac\"me", weirdRow[3]);                    // embedded quote survives escaping
        Assert.Single(rows, r => r[0] == plain.FilePath);
    }
}
