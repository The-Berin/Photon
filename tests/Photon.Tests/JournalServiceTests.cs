using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

public class JournalServiceTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly JournalService _js;

    public JournalServiceTests() => _js = TestServices.Journal(_t.Dir("journals"));

    public void Dispose() => _t.Dispose();

    private SortJournal NewJournal(SortAction action, string kind = "Sort",
        DateTime? stamp = null, string? backupFolder = null) => new()
    {
        TimestampUtc = stamp ?? new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        OperationKind = kind,
        SourceFolder = _t.At("src"),
        DestinationRoot = _t.At("dest"),
        Action = action,
        BackupFolder = backupFolder,
    };

    [Fact]
    public async Task SaveAsync_WritesJsonIntoJournalDirectory()
    {
        var j = NewJournal(SortAction.Copy);
        await _js.SaveAsync(j);

        Assert.NotEmpty(Directory.GetFiles(_js.JournalDirectory, "*.json"));
    }

    [Fact]
    public async Task LoadAll_NewestFirst_ToleratesCorruptJson()
    {
        var oldest = NewJournal(SortAction.Copy, stamp: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var middle = NewJournal(SortAction.Copy, stamp: new DateTime(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = NewJournal(SortAction.Copy, stamp: new DateTime(2023, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await _js.SaveAsync(middle);
        await _js.SaveAsync(oldest);
        await _js.SaveAsync(newest);

        File.WriteAllText(Path.Combine(_js.JournalDirectory, "zz-corrupt.json"), "{ not valid json !!!");

        var all = _js.LoadAll();

        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { newest.Id, middle.Id, oldest.Id }, all.Select(j => j.Id).ToArray());
    }

    [Fact]
    public async Task LoadLatestUndoable_SkipsUndoneJournals()
    {
        var older = NewJournal(SortAction.Copy, stamp: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newerUndone = NewJournal(SortAction.Copy, stamp: new DateTime(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        newerUndone.UndoneAtUtc = new DateTime(2023, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        await _js.SaveAsync(older);
        await _js.SaveAsync(newerUndone);

        var latest = _js.LoadLatestUndoable();

        Assert.NotNull(latest);
        Assert.Equal(older.Id, latest!.Id);
    }

    [Fact]
    public void LoadLatestUndoable_EmptyDirectory_ReturnsNull()
        => Assert.Null(_js.LoadLatestUndoable());

    [Fact]
    public async Task UndoCopyRun_DeletesCopies_PrunesCreatedEmptyDirs()
    {
        var source = _t.File("src/a.jpg", 300);
        var copy = _t.File("dest/2023/March/05/a.jpg", File.ReadAllBytes(source));
        var sourceContent = File.ReadAllBytes(source);

        var j = NewJournal(SortAction.Copy);
        j.Entries.Add(new JournalEntry
        {
            Operation = JournalOperation.Copied,
            OriginalPath = source,
            NewPath = copy,
            SizeBytes = 300,
        });
        // Deepest first, as the contract requires.
        j.CreatedDirectories.Add(_t.At("dest/2023/March/05"));
        j.CreatedDirectories.Add(_t.At("dest/2023/March"));
        j.CreatedDirectories.Add(_t.At("dest/2023"));
        await _js.SaveAsync(j);

        var result = await _js.UndoAsync(j);

        Assert.Equal(1, result.Reversed);
        Assert.False(File.Exists(copy));                               // copy removed
        Assert.True(File.Exists(source));                              // source never touched
        Assert.Equal(sourceContent, File.ReadAllBytes(source));
        Assert.False(Directory.Exists(_t.At("dest/2023")));            // empty tree pruned
        Assert.True(result.DirectoriesRemoved >= 3);
        Assert.NotNull(j.UndoneAtUtc);
    }

    [Fact]
    public async Task UndoMoveRun_RestoresEveryFile()
    {
        var contentA = _t.Bytes(200);
        var contentB = _t.Bytes(200);
        _t.Dir("src");
        var movedA = _t.File("dest/2023/a.jpg", contentA);
        var movedB = _t.File("dest/2023/b.jpg", contentB);

        var j = NewJournal(SortAction.Move);
        j.Entries.Add(new JournalEntry { Operation = JournalOperation.Moved, OriginalPath = _t.At("src/a.jpg"), NewPath = movedA, SizeBytes = 200 });
        j.Entries.Add(new JournalEntry { Operation = JournalOperation.Moved, OriginalPath = _t.At("src/b.jpg"), NewPath = movedB, SizeBytes = 200 });
        j.CreatedDirectories.Add(_t.At("dest/2023"));
        await _js.SaveAsync(j);

        var result = await _js.UndoAsync(j);

        Assert.Equal(2, result.Reversed);
        Assert.Empty(result.Errors);
        Assert.Equal(contentA, File.ReadAllBytes(_t.At("src/a.jpg")));
        Assert.Equal(contentB, File.ReadAllBytes(_t.At("src/b.jpg")));
        Assert.False(File.Exists(movedA));
        Assert.False(File.Exists(movedB));
    }

    [Fact]
    public async Task UndoOverwrite_RestoresDisplacedBackupToDestination()
    {
        var newContent = _t.Bytes(150);
        var oldContent = _t.Bytes(150);
        var source = _t.File("src/f.jpg", newContent);
        var dest = _t.File("dest/f.jpg", newContent);              // the overwriting copy
        var backupDir = _t.Dir("journals", "backup");
        var backup = _t.File("journals/backup/f.jpg", oldContent); // the displaced original

        var j = NewJournal(SortAction.Copy, backupFolder: backupDir);
        j.Entries.Add(new JournalEntry
        {
            Operation = JournalOperation.Overwrote,
            OriginalPath = source,
            NewPath = dest,
            DisplacedBackupPath = backup,
            SizeBytes = 150,
        });
        await _js.SaveAsync(j);

        var result = await _js.UndoAsync(j);

        Assert.Equal(oldContent, File.ReadAllBytes(dest));         // displaced file back in place
        Assert.True(File.Exists(source));                          // copy-run source untouched
        Assert.Equal(newContent, File.ReadAllBytes(source));
        Assert.True(result.RestoredFromBackup >= 1);
    }

    [Fact]
    public async Task UndoRenameJournal_RestoresOriginalNames()
    {
        var content = _t.Bytes(100);
        var renamed = _t.File("src/renamed_001.jpg", content);

        var j = NewJournal(SortAction.Move, kind: "Batch rename");
        j.Entries.Add(new JournalEntry
        {
            Operation = JournalOperation.RenamedInPlace,
            OriginalPath = _t.At("src/IMG_0001.jpg"),
            NewPath = renamed,
            SizeBytes = 100,
        });
        await _js.SaveAsync(j);

        await _js.UndoAsync(j);

        Assert.True(File.Exists(_t.At("src/IMG_0001.jpg")));
        Assert.Equal(content, File.ReadAllBytes(_t.At("src/IMG_0001.jpg")));
        Assert.False(File.Exists(renamed));
    }

    [Fact]
    public async Task UndoneFlag_PersistsAcrossReload()
    {
        var source = _t.File("src/a.jpg", 100);
        var copy = _t.File("dest/a.jpg", File.ReadAllBytes(source));
        var j = NewJournal(SortAction.Copy);
        j.Entries.Add(new JournalEntry { Operation = JournalOperation.Copied, OriginalPath = source, NewPath = copy, SizeBytes = 100 });
        await _js.SaveAsync(j);

        await _js.UndoAsync(j);

        var reloaded = Assert.Single(_js.LoadAll());
        Assert.NotNull(reloaded.UndoneAtUtc);
        Assert.Null(_js.LoadLatestUndoable());   // nothing left to undo
    }

    private JournalEntry CopiedEntry(string source, string copy, long size) => new()
    {
        Operation = JournalOperation.Copied,
        OriginalPath = source,
        NewPath = copy,
        SizeBytes = size,
        NewFileWriteTimeUtc = File.GetLastWriteTimeUtc(copy),
    };

    [Fact]
    public async Task UndoCopy_DestinationEditedAfterSort_StashedNotDeleted()
    {
        var source = _t.File("src/a.jpg", 300);
        var copy = _t.File("dest/a.jpg", File.ReadAllBytes(source));
        var backupDir = _t.At("journals", "backup");

        var j = NewJournal(SortAction.Copy, backupFolder: backupDir);
        j.Entries.Add(CopiedEntry(source, copy, 300));
        await _js.SaveAsync(j);

        // The user edits the copy after the sort; undo must not hard-delete their work.
        File.WriteAllBytes(copy, _t.Bytes(310));
        var edited = File.ReadAllBytes(copy);

        var result = await _js.UndoAsync(j);

        Assert.False(File.Exists(copy));                        // gone from the destination...
        var stash = Assert.Single(Directory.GetFiles(backupDir));
        Assert.Equal(edited, File.ReadAllBytes(stash));         // ...but preserved, not deleted
        Assert.True(File.Exists(source));
        Assert.Contains(result.Errors, e => e.Error.Contains("preserved"));
    }

    [Fact]
    public async Task UndoCopy_DestinationUnmodified_DeletedNormally()
    {
        var source = _t.File("src/a.jpg", 300);
        var copy = _t.File("dest/a.jpg", File.ReadAllBytes(source));
        var backupDir = _t.At("journals", "backup");

        var j = NewJournal(SortAction.Copy, backupFolder: backupDir);
        j.Entries.Add(CopiedEntry(source, copy, 300));
        await _js.SaveAsync(j);

        var result = await _js.UndoAsync(j);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Reversed);
        Assert.False(File.Exists(copy));
        Assert.False(Directory.Exists(backupDir));              // nothing was stashed
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task UndoCaseOnlyRename_RestoresOriginalCasing_NoSuffix()
    {
        // RenameEngine case transforms rename IMG_001.JPG -> img_001.jpg via a temp hop;
        // undo must not mistake the renamed file for an occupant of the original path.
        var content = _t.Bytes(90);
        var renamed = _t.File("src/img_001.jpg", content);

        var j = NewJournal(SortAction.Move, kind: "Batch rename");
        j.Entries.Add(new JournalEntry
        {
            Operation = JournalOperation.RenamedInPlace,
            OriginalPath = _t.At("src/IMG_001.JPG"),
            NewPath = renamed,
            SizeBytes = 90,
        });
        await _js.SaveAsync(j);

        var result = await _js.UndoAsync(j);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Reversed);
        var only = Assert.Single(Directory.GetFiles(_t.At("src")));
        Assert.Equal("IMG_001.JPG", Path.GetFileName(only));   // exact original casing, no _1
        Assert.Equal(content, File.ReadAllBytes(only));
    }

    [Fact]
    public async Task Undo_EntryFails_JournalStaysRetryable()
    {
        // The moved file is missing (e.g. its drive is unplugged): nothing gets restored,
        // so the journal must NOT be marked undone — the user can retry later.
        _t.Dir("src");
        var j = NewJournal(SortAction.Move);
        j.Entries.Add(new JournalEntry
        {
            Operation = JournalOperation.Moved,
            OriginalPath = _t.At("src/a.jpg"),
            NewPath = _t.At("dest/a.jpg"),      // never created
            SizeBytes = 100,
        });
        await _js.SaveAsync(j);

        var result = await _js.UndoAsync(j);

        Assert.Equal(0, result.Reversed);
        Assert.Single(result.Errors);
        Assert.Null(j.UndoneAtUtc);
        var reloaded = _js.LoadLatestUndoable();
        Assert.NotNull(reloaded);
        Assert.Equal(j.Id, reloaded!.Id);      // History still offers the retry
    }

    [Fact]
    public async Task CancelledUndo_Retry_DoesNotDeleteRestoredOverwriteBackup()
    {
        // Copy-run journal: [Copied, Overwrote]. Undo runs in reverse, so the Overwrote
        // entry is processed first; cancelling right after it must leave a retry that
        // skips the entry instead of deleting the file the first pass just restored.
        var copySource = _t.File("src/a.jpg", 100);
        var copyDest = _t.File("dest/a.jpg", File.ReadAllBytes(copySource));
        var newContent = _t.Bytes(150);
        var oldContent = _t.Bytes(150);
        var overwriteSource = _t.File("src/f.jpg", newContent);
        var overwriteDest = _t.File("dest/f.jpg", newContent);
        var backupDir = _t.Dir("journals", "backup");
        var backup = _t.File("journals/backup/f.jpg", oldContent);

        var j = NewJournal(SortAction.Copy, backupFolder: backupDir);
        j.Entries.Add(new JournalEntry { Operation = JournalOperation.Copied, OriginalPath = copySource, NewPath = copyDest, SizeBytes = 100 });
        j.Entries.Add(new JournalEntry
        {
            Operation = JournalOperation.Overwrote,
            OriginalPath = overwriteSource,
            NewPath = overwriteDest,
            DisplacedBackupPath = backup,
            SizeBytes = 150,
        });
        await _js.SaveAsync(j);

        // Pass 1: cancel after the first (Overwrote) entry is undone.
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<SortProgress>(_ => cts.Cancel());
        var first = await _js.UndoAsync(j, progress, cts.Token);

        Assert.True(first.Cancelled);
        Assert.Equal(oldContent, File.ReadAllBytes(overwriteDest));   // displaced file restored
        Assert.False(File.Exists(backup));                            // backup consumed
        Assert.True(File.Exists(copyDest));                           // not reached yet

        // Pass 2 (retry): must not replay the Overwrote entry.
        var second = await _js.UndoAsync(j);

        Assert.Empty(second.Errors);
        Assert.True(File.Exists(overwriteDest));                      // restored file SURVIVES the retry
        Assert.Equal(oldContent, File.ReadAllBytes(overwriteDest));
        Assert.False(File.Exists(copyDest));                          // the Copied entry got its turn
        Assert.NotNull(j.UndoneAtUtc);
    }

    [Fact]
    public async Task DoubleUndo_NoOpOrCleanError_NeverDataLoss()
    {
        var sourceContent = _t.Bytes(250);
        _t.Dir("src");
        var moved = _t.File("dest/a.jpg", sourceContent);
        var j = NewJournal(SortAction.Move);
        j.Entries.Add(new JournalEntry { Operation = JournalOperation.Moved, OriginalPath = _t.At("src/a.jpg"), NewPath = moved, SizeBytes = 250 });
        await _js.SaveAsync(j);

        await _js.UndoAsync(j);
        Assert.Equal(sourceContent, File.ReadAllBytes(_t.At("src/a.jpg")));

        // Second undo of the same journal: allowed to no-op or throw, but the file must survive intact.
        try { await _js.UndoAsync(j); }
        catch { /* a clean error is acceptable */ }

        Assert.True(File.Exists(_t.At("src/a.jpg")));
        Assert.Equal(sourceContent, File.ReadAllBytes(_t.At("src/a.jpg")));
    }
}
