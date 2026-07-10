using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

public class RenameEngineTests : InvariantCultureTest, IDisposable
{
    private readonly TempDir _t = new();
    private readonly JournalService _journal;
    private readonly RenameEngine _engine;

    public RenameEngineTests()
    {
        _journal = TestServices.Journal(_t.Dir("journals"));
        _engine = TestServices.RenameEngine(_journal);
    }

    public void Dispose() => _t.Dispose();

    private static string NewNameFor(List<RenamePlanItem> plan, string oldPath)
        => plan.Single(i => i.OldPath == oldPath).NewName;

    // ---------- tokens ----------

    [Fact]
    public void DefaultOptions_AreANoOp()
    {
        var f = _t.File("src/My Photo.jpg");
        var plan = _engine.BuildPlan([f], new RenameOptions());

        var item = Assert.Single(plan);
        Assert.Equal("My Photo.jpg", item.NewName);
        Assert.False(item.Changed);
        Assert.Null(item.Problem);
    }

    [Fact]
    public void CounterToken_HonorsStartStepAndPadding()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions { Pattern = "img_{counter}", CounterStart = 5, CounterStep = 10, CounterPadding = 4 };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Equal("img_0005.jpg", NewNameFor(plan, a));
        Assert.Equal("img_0015.jpg", NewNameFor(plan, b));
    }

    [Fact]
    public void CounterToken_PerFolder_RestartsInEachSubfolder()
    {
        var a = _t.File("d1/a.jpg");
        var b = _t.File("d1/b.jpg");
        var c = _t.File("d2/c.jpg");
        var o = new RenameOptions { Pattern = "s_{counter}", CounterPadding = 2, CounterPerFolder = true };

        var plan = _engine.BuildPlan([a, b, c], o);

        Assert.Equal("s_01.jpg", NewNameFor(plan, a));
        Assert.Equal("s_02.jpg", NewNameFor(plan, b));
        Assert.Equal("s_01.jpg", NewNameFor(plan, c));   // restarted in d2
    }

    [Fact]
    public void DateTokens_ComeFromFileDates()
    {
        var f = _t.File("src/pic.jpg");
        // min(created, modified) = this mtime, well before the file's creation time (now).
        File.SetLastWriteTime(f, new DateTime(2020, 5, 6, 7, 8, 9));
        var o = new RenameOptions { Pattern = "{yyyy}-{MM}-{dd}", DateSource = DateSource.FileDateOnly };

        var plan = _engine.BuildPlan([f], o);

        Assert.Equal("2020-05-06.jpg", Assert.Single(plan).NewName);
    }

    [Fact]
    public void ParentToken_UsesContainingFolderName()
    {
        var f = _t.File("Holiday/photo.jpg");
        var plan = _engine.BuildPlan([f], new RenameOptions { Pattern = "{parent}_{name}" });

        Assert.Equal("Holiday_photo.jpg", Assert.Single(plan).NewName);
    }

    [Fact]
    public void SizeToken_EmitsHumanReadableSize()
    {
        var f = _t.File("src/photo.jpg", 2048);
        var plan = _engine.BuildPlan([f], new RenameOptions { Pattern = "{name}_{size}" });

        Assert.Equal("photo_2.00 KB.jpg", Assert.Single(plan).NewName);
    }

    [Fact]
    public void ExtToken_ExpandsToExtensionWithoutDot()
    {
        // The pattern shapes the stem; the original extension is always re-appended.
        var f = _t.File("src/photo.jpg");
        var plan = _engine.BuildPlan([f], new RenameOptions { Pattern = "{ext}_{name}" });

        Assert.Equal("jpg_photo.jpg", Assert.Single(plan).NewName);
    }

    [Fact]
    public void NameToken_UnknownTokensLeftVerbatim()
    {
        var f = _t.File("src/photo.jpg");
        var plan = _engine.BuildPlan([f], new RenameOptions { Pattern = "{name}{bogus}" });

        Assert.Equal("photo{bogus}.jpg", Assert.Single(plan).NewName);
    }

    // ---------- pipeline order ----------

    [Fact]
    public void Pipeline_PatternThenReplaceThenCaseThenPrefix()
    {
        var f = _t.File("src/photo.jpg");
        var o = new RenameOptions
        {
            Pattern = "AB_{name}",       // "AB_photo" — the AB only exists post-pattern
            NameCase = CaseTransform.Lower,
            Prefix = "X-",               // applied after the case transform, so X stays upper
        };
        o.Replacements.Add(new FindReplaceRule { Find = "AB", Replace = "CD", CaseSensitive = true });

        var plan = _engine.BuildPlan([f], o);

        // pattern -> "AB_photo", replace -> "CD_photo", lower -> "cd_photo", prefix -> "X-cd_photo"
        Assert.Equal("X-cd_photo.jpg", Assert.Single(plan).NewName);
    }

    // ---------- find/replace ----------

    [Fact]
    public void LiteralReplace_CaseInsensitiveByDefault()
    {
        var f = _t.File("src/IMG_001.jpg");
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = "img", Replace = "pic" });

        Assert.Equal("pic_001.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void LiteralReplace_CaseSensitive_NoMatchLeavesName()
    {
        var f = _t.File("src/IMG_001.jpg");
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = "img", Replace = "pic", CaseSensitive = true });

        Assert.Equal("IMG_001.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void RegexReplace_Works()
    {
        var f = _t.File("src/photo123take7.jpg");
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = @"\d+", Replace = "#", UseRegex = true });

        Assert.Equal("photo#take#.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void InvalidRegex_ReportsProblemInsteadOfThrowing()
    {
        var f = _t.File("src/photo.jpg");
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = "[unclosed", Replace = "x", UseRegex = true });

        var plan = _engine.BuildPlan([f], o);

        Assert.NotNull(Assert.Single(plan).Problem);
    }

    [Fact]
    public void DisabledReplacement_IsIgnored()
    {
        var f = _t.File("src/IMG_001.jpg");
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = "IMG", Replace = "pic", Enabled = false });

        Assert.Equal("IMG_001.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    // ---------- remove range ----------

    [Fact]
    public void RemoveRange_FromStart()
    {
        var f = _t.File("src/IMG_1234.jpg");
        var o = new RenameOptions { RemoveRange = new RemoveRangeRule { Start = 0, Count = 4, Enabled = true } };

        Assert.Equal("1234.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void RemoveRange_FromEnd()
    {
        var f = _t.File("src/IMG_1234.jpg");
        var o = new RenameOptions { RemoveRange = new RemoveRangeRule { Start = 0, Count = 4, FromEnd = true, Enabled = true } };

        Assert.Equal("IMG_.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    // ---------- character cleanup ----------

    [Fact]
    public void RemoveDiacritics_FoldsToAscii()
    {
        var f = _t.File("src/café naïve.jpg");
        var o = new RenameOptions { RemoveDiacritics = true };

        Assert.Equal("cafe naive.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    // ---------- case transforms ----------

    [Fact]
    public void NameCase_Upper_LeavesExtensionAlone()
    {
        var f = _t.File("src/photo.jpg");
        var o = new RenameOptions { NameCase = CaseTransform.Upper };

        Assert.Equal("PHOTO.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void NameCase_TitleCase()
    {
        var f = _t.File("src/my summer trip.jpg");
        var o = new RenameOptions { NameCase = CaseTransform.TitleCase };

        Assert.Equal("My Summer Trip.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void ExtensionCase_Only_TransformsJustTheExtension()
    {
        var f = _t.File("src/photo.jpg");
        var o = new RenameOptions { ExtensionCase = CaseTransform.Upper };

        Assert.Equal("photo.JPG", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    // ---------- collisions ----------

    [Fact]
    public void CollisionWithinPlan_AppendNumber()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions { Pattern = "same", ConflictPolicy = RenameConflictPolicy.AppendNumber };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Equal(new HashSet<string> { "same.jpg", "same_1.jpg" },
            plan.Select(i => i.NewName).ToHashSet());
        Assert.All(plan, i => Assert.Null(i.Problem));
    }

    [Fact]
    public void CollisionWithinPlan_FailPolicy_MarksProblem()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions { Pattern = "same", ConflictPolicy = RenameConflictPolicy.Fail };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Contains(plan, i => i.Problem is not null);
    }

    // ---------- masks ----------

    [Fact]
    public void IncludeMask_LimitsScope()
    {
        var img = _t.File("src/IMG_1.jpg");
        var dsc = _t.File("src/DSC_1.jpg");
        var o = new RenameOptions { Pattern = "X_{counter}", IncludeMask = "IMG_*.jpg" };

        var plan = _engine.BuildPlan([img, dsc], o);

        var imgItem = plan.Single(i => i.OldPath == img);
        Assert.True(imgItem.Changed);

        // The masked-out file is either omitted from the plan or left unchanged.
        var dscItem = plan.SingleOrDefault(i => i.OldPath == dsc);
        Assert.True(dscItem is null || !dscItem.Changed);
    }

    // ---------- execution + undo ----------

    [Fact]
    public async Task ExecuteAsync_RenamesOnDisk_AndJournalUndoRestores()
    {
        var contentA = _t.Bytes(100);
        var contentB = _t.Bytes(100);
        var a = _t.File("src/IMG_0001.jpg", contentA);
        var b = _t.File("src/IMG_0002.jpg", contentB);
        var o = new RenameOptions { Pattern = "photo_{counter}" };

        var plan = _engine.BuildPlan([a, b], o);
        var result = await _engine.ExecuteAsync(plan, o);

        Assert.Equal(2, result.Renamed);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.JournalPath);
        Assert.True(File.Exists(result.JournalPath));
        Assert.False(File.Exists(a));
        Assert.Equal(contentA, File.ReadAllBytes(_t.At("src/photo_001.jpg")));
        Assert.Equal(contentB, File.ReadAllBytes(_t.At("src/photo_002.jpg")));

        var journal = _journal.LoadLatestUndoable();
        Assert.NotNull(journal);
        await _journal.UndoAsync(journal!);

        Assert.Equal(contentA, File.ReadAllBytes(a));
        Assert.Equal(contentB, File.ReadAllBytes(b));
        Assert.False(File.Exists(_t.At("src/photo_001.jpg")));
    }

    [Fact]
    public async Task CaseOnlyRename_WorksOnCaseInsensitiveVolume()
    {
        var f = _t.File("src/photo.jpg");
        var o = new RenameOptions { NameCase = CaseTransform.Upper };

        var plan = _engine.BuildPlan([f], o);
        Assert.Equal("PHOTO.jpg", Assert.Single(plan).NewName);
        Assert.True(plan[0].Changed);   // ordinal comparison: a case-only change counts

        var result = await _engine.ExecuteAsync(plan, o);

        Assert.Equal(1, result.Renamed);
        Assert.Empty(result.Errors);
        // On a case-insensitive volume File.Exists can't tell the difference — read the real entry name.
        var actualName = Path.GetFileName(Assert.Single(Directory.GetFiles(_t.At("src"))));
        Assert.Equal("PHOTO.jpg", actualName);
    }
}
