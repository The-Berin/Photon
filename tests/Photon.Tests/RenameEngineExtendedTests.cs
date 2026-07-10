using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

/// <summary>
/// Coverage for the expanded rename option set: numbering styles and orders, swap,
/// find/replace flags, the remove family, anchored inserts, hygiene, case transforms,
/// affixes, extension operations, scope filters, collision templates and the mapping CSV.
/// </summary>
public class RenameEngineExtendedTests : InvariantCultureTest, IDisposable
{
    private readonly TempDir _t = new();
    private readonly JournalService _journal;
    private readonly RenameEngine _engine;

    public RenameEngineExtendedTests()
    {
        _journal = TestServices.Journal(_t.Dir("journals"));
        _engine = TestServices.RenameEngine(_journal);
    }

    public void Dispose() => _t.Dispose();

    private static string NewNameFor(List<RenamePlanItem> plan, string oldPath)
        => plan.Single(i => i.OldPath == oldPath).NewName;

    private string PlanSingle(string relativePath, RenameOptions options)
    {
        var f = _t.File(relativePath);
        return Assert.Single(_engine.BuildPlan([f], options)).NewName;
    }

    // ---------- counter styles ----------

    [Fact]
    public void CounterStyle_AlphaLower_RollsOverAtZ()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var c = _t.File("src/c.jpg");
        var o = new RenameOptions { Pattern = "{counter}", CounterStart = 25, CounterStyle = CounterStyle.AlphaLower };

        var plan = _engine.BuildPlan([a, b, c], o);

        Assert.Equal("y.jpg", NewNameFor(plan, a));
        Assert.Equal("z.jpg", NewNameFor(plan, b));
        Assert.Equal("aa.jpg", NewNameFor(plan, c));
    }

    [Fact]
    public void CounterStyle_AlphaUpper()
    {
        var a = _t.File("src/a.jpg");
        var o = new RenameOptions { Pattern = "{counter}", CounterStart = 28, CounterStyle = CounterStyle.AlphaUpper };

        Assert.Equal("AB.jpg", Assert.Single(_engine.BuildPlan([a], o)).NewName);
    }

    [Fact]
    public void CounterStyle_Roman_StandardNumerals_AndZeroFallsBack()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions { Pattern = "{counter}", CounterStart = 0, CounterStep = 4, CounterStyle = CounterStyle.RomanUpper };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Equal("0.jpg", NewNameFor(plan, a));   // no roman zero
        Assert.Equal("IV.jpg", NewNameFor(plan, b));
    }

    [Fact]
    public void CounterStyle_RomanLower()
    {
        var a = _t.File("src/a.jpg");
        var o = new RenameOptions { Pattern = "{counter}", CounterStart = 1949, CounterStyle = CounterStyle.RomanLower };

        Assert.Equal("mcmxlix.jpg", Assert.Single(_engine.BuildPlan([a], o)).NewName);
    }

    [Fact]
    public void CounterStyle_Hex_RespectsPadding()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions { Pattern = "{counter}", CounterStart = 255, CounterStyle = CounterStyle.HexLower, CounterPadding = 4 };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Equal("00ff.jpg", NewNameFor(plan, a));
        Assert.Equal("0100.jpg", NewNameFor(plan, b));

        o.CounterStyle = CounterStyle.HexUpper;
        Assert.Equal("00FF.jpg", NewNameFor(_engine.BuildPlan([a, b], o), a));
    }

    [Fact]
    public void Counter2_HonorsOwnStartStepAndPadding()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions
        {
            Pattern = "{counter}_{counter2}",
            CounterStart = 1, CounterPadding = 3,
            Counter2Start = 10, Counter2Step = 5, Counter2Padding = 2,
        };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Equal("001_10.jpg", NewNameFor(plan, a));
        Assert.Equal("002_15.jpg", NewNameFor(plan, b));
    }

    [Fact]
    public void Counter2_NeverResetsPerFolder()
    {
        var a = _t.File("d1/a.jpg");
        var b = _t.File("d1/b.jpg");
        var c = _t.File("d2/c.jpg");
        var o = new RenameOptions
        {
            Pattern = "{counter}-{counter2}",
            CounterPadding = 2, Counter2Padding = 2,
            CounterPerFolder = true,
        };

        var plan = _engine.BuildPlan([a, b, c], o);

        Assert.Equal("01-01.jpg", NewNameFor(plan, a));
        Assert.Equal("02-02.jpg", NewNameFor(plan, b));
        Assert.Equal("01-03.jpg", NewNameFor(plan, c)); // {counter} restarted, {counter2} kept going
    }

    // ---------- numbering order ----------

    [Fact]
    public void NumberingOrder_NameAscending_AssignsByName_KeepsPlanInListedOrder()
    {
        var z = _t.File("src/zebra.jpg");
        var a = _t.File("src/apple.jpg");
        var o = new RenameOptions { Pattern = "n{counter}", NumberingOrder = NumberingOrder.NameAscending };

        var plan = _engine.BuildPlan([z, a], o);

        Assert.Equal("n002.jpg", NewNameFor(plan, z));
        Assert.Equal("n001.jpg", NewNameFor(plan, a));
        Assert.Equal([z, a], plan.Select(i => i.OldPath)); // plan rows stay in input order
    }

    [Fact]
    public void NumberingOrder_NameDescending()
    {
        var z = _t.File("src/zebra.jpg");
        var a = _t.File("src/apple.jpg");
        var o = new RenameOptions { Pattern = "n{counter}", NumberingOrder = NumberingOrder.NameDescending };

        var plan = _engine.BuildPlan([z, a], o);

        Assert.Equal("n001.jpg", NewNameFor(plan, z));
        Assert.Equal("n002.jpg", NewNameFor(plan, a));
    }

    [Fact]
    public void NumberingOrder_SizeAscendingAndDescending()
    {
        var big = _t.File("src/big.jpg", 5000);
        var small = _t.File("src/small.jpg", 100);

        var asc = _engine.BuildPlan([big, small],
            new RenameOptions { Pattern = "n{counter}", NumberingOrder = NumberingOrder.SizeAscending });
        Assert.Equal("n001.jpg", NewNameFor(asc, small));
        Assert.Equal("n002.jpg", NewNameFor(asc, big));

        var desc = _engine.BuildPlan([big, small],
            new RenameOptions { Pattern = "n{counter}", NumberingOrder = NumberingOrder.SizeDescending });
        Assert.Equal("n001.jpg", NewNameFor(desc, big));
        Assert.Equal("n002.jpg", NewNameFor(desc, small));
    }

    [Fact]
    public void NumberingOrder_DateAscending_UsesEarlierOfCreatedAndModified()
    {
        var newer = _t.File("src/newer.jpg");
        var older = _t.File("src/older.jpg");
        File.SetLastWriteTime(newer, new DateTime(2024, 6, 1));
        File.SetLastWriteTime(older, new DateTime(2020, 1, 1));
        var o = new RenameOptions { Pattern = "n{counter}", NumberingOrder = NumberingOrder.DateAscending };

        var plan = _engine.BuildPlan([newer, older], o);

        Assert.Equal("n001.jpg", NewNameFor(plan, older));
        Assert.Equal("n002.jpg", NewNameFor(plan, newer));
    }

    [Fact]
    public void NumberingOrder_PathAscending()
    {
        var b = _t.File("beta/pic.jpg");
        var a = _t.File("alpha/pic.jpg");
        var o = new RenameOptions { Pattern = "n{counter}", NumberingOrder = NumberingOrder.PathAscending };

        var plan = _engine.BuildPlan([b, a], o);

        Assert.Equal("n001.jpg", NewNameFor(plan, a));
        Assert.Equal("n002.jpg", NewNameFor(plan, b));
    }

    // ---------- swap ----------

    [Fact]
    public void Swap_ExchangesHalvesAroundFirstSeparator()
    {
        var o = new RenameOptions { SwapEnabled = true, SwapSeparator = " - " };
        Assert.Equal("Beach - 2023.jpg", PlanSingle("src/2023 - Beach.jpg", o));
    }

    [Fact]
    public void Swap_FirstOccurrenceOnly()
    {
        var o = new RenameOptions { SwapEnabled = true, SwapSeparator = "-" };
        Assert.Equal("b-c-a.jpg", PlanSingle("src/a-b-c.jpg", o));
    }

    [Fact]
    public void Swap_MissingSeparator_IsANoOp()
    {
        var o = new RenameOptions { SwapEnabled = true, SwapSeparator = " - " };
        Assert.Equal("plain.jpg", PlanSingle("src/plain.jpg", o));
    }

    [Fact]
    public void Swap_RunsBeforeReplacements()
    {
        var o = new RenameOptions { SwapEnabled = true, SwapSeparator = "_" };
        o.Replacements.Add(new FindReplaceRule { Find = "b_", Replace = "X" });
        // "a_b" → swap → "b_a" → replace "b_" → "Xa"
        Assert.Equal("Xa.jpg", PlanSingle("src/a_b.jpg", o));
    }

    // ---------- find & replace flags ----------

    [Fact]
    public void Replace_WholeWord_MatchesWholeWordsOnly()
    {
        var o = new RenameOptions { CollapseSpaces = true };
        o.Replacements.Add(new FindReplaceRule { Find = "copy", Replace = "", WholeWord = true });

        Assert.Equal("report final.jpg", PlanSingle("src/report copy final.jpg", o));
        // "copyright" must survive a whole-word "copy"
        Assert.Equal("copyright.jpg", PlanSingle("src/copyright.jpg", o));
    }

    [Fact]
    public void Replace_FirstOnly_Literal()
    {
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = "a", Replace = "X", FirstOnly = true });

        Assert.Equal("bXnana.jpg", PlanSingle("src/banana.jpg", o));
    }

    [Fact]
    public void Replace_FirstOnly_Regex()
    {
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = @"\d+", Replace = "#", UseRegex = true, FirstOnly = true });

        Assert.Equal("p#q22.jpg", PlanSingle("src/p11q22.jpg", o));
    }

    [Fact]
    public void Replace_Target_ExtensionOnly()
    {
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = "jpeg", Replace = "jpg", Target = ReplaceTarget.ExtensionOnly });

        Assert.Equal("jpeg-scan.jpg", PlanSingle("src/jpeg-scan.jpeg", o));
    }

    [Fact]
    public void Replace_Target_Both()
    {
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = "e", Replace = "3", Target = ReplaceTarget.Both });

        Assert.Equal("t3st.jp3g", PlanSingle("src/test.jpeg", o));
    }

    [Fact]
    public void Replace_WholeWord_ReplacementDollarSignsStayLiteral()
    {
        var o = new RenameOptions();
        o.Replacements.Add(new FindReplaceRule { Find = "price", Replace = "$1", WholeWord = true });

        Assert.Equal("the $1 list.jpg", PlanSingle("src/the price list.jpg", o));
    }

    // ---------- removes ----------

    [Fact]
    public void RemoveRange2_AppliesAfterRemoveRange()
    {
        var o = new RenameOptions
        {
            RemoveRange = new RemoveRangeRule { Start = 0, Count = 4, Enabled = true },   // "IMG_" gone
            RemoveRange2 = new RemoveRangeRule { Start = 0, Count = 2, FromEnd = true, Enabled = true },
        };
        Assert.Equal("12.jpg", PlanSingle("src/IMG_1234.jpg", o));
    }

    [Fact]
    public void RemoveBetween_IncludingDelimiters()
    {
        var o = new RenameOptions { RemoveBetween = new RemoveBetweenRule { From = " (", To = ")", Enabled = true } };
        Assert.Equal("pic x.jpg", PlanSingle("src/pic (copy) x.jpg", o));
    }

    [Fact]
    public void RemoveBetween_KeepingDelimiters()
    {
        var o = new RenameOptions
        {
            RemoveBetween = new RemoveBetweenRule { From = "[", To = "]", IncludeDelimiters = false, Enabled = true },
        };
        Assert.Equal("take[] 2.jpg", PlanSingle("src/take[old] 2.jpg", o));
    }

    [Fact]
    public void RemoveBetween_MissingCloser_IsANoOp()
    {
        var o = new RenameOptions { RemoveBetween = new RemoveBetweenRule { From = "[", To = "]", Enabled = true } };
        Assert.Equal("pic [open.jpg", PlanSingle("src/pic [open.jpg", o));
    }

    [Theory]
    [InlineData("IMG_1234.jpg", "1234.jpg")]
    [InlineData("img-005.jpg", "005.jpg")]              // case-insensitive
    [InlineData("IMG_IMG_007.jpg", "007.jpg")]          // strips repeatedly
    [InlineData("PXL_20240601.jpg", "20240601.jpg")]
    [InlineData("Screenshot_dashboard.png", "dashboard.png")]
    [InlineData("Screen Shot 9.15.32.png", "9.15.32.png")]
    [InlineData("DSCN0001.jpg", "0001.jpg")]
    [InlineData("VID_0042.mp4", "0042.mp4")]
    [InlineData("keeper.jpg", "keeper.jpg")]
    public void RemoveCameraPrefixes_StripsKnownPrefixesFromStart(string name, string expected)
    {
        var o = new RenameOptions { RemoveCameraPrefixes = true };
        Assert.Equal(expected, PlanSingle("src/" + name, o));
    }

    [Theory]
    [InlineData("trip 20240601 pics.jpg", "trip  pics.jpg")]
    [InlineData("trip 2024-06-01 pics.jpg", "trip  pics.jpg")]
    [InlineData("trip 2024_06_01 pics.jpg", "trip  pics.jpg")]
    [InlineData("trip 01.06.2024 pics.jpg", "trip  pics.jpg")]
    [InlineData("part 12345678 kept.jpg", "part 12345678 kept.jpg")] // not a plausible date
    public void RemoveDatePatterns_StripsDateLikeRuns(string name, string expected)
    {
        var o = new RenameOptions { RemoveDatePatterns = true, TrimWhitespace = false };
        Assert.Equal(expected, PlanSingle("src/" + name, o));
    }

    [Fact]
    public void RemoveGuidPatterns_StripsGuidRuns()
    {
        var o = new RenameOptions { RemoveGuidPatterns = true };
        Assert.Equal("export-.jpg", PlanSingle("src/export-D41D8CD9-8F00-3204-A980-0998ECF8427E.jpg", o));
    }

    [Fact]
    public void RemoveUrls_StripsHttpAndWwwRuns()
    {
        // URLs with slashes cannot exist in on-disk names, so the http case rides in via the pattern.
        var f = _t.File("src/photo.jpg");
        var o = new RenameOptions { Pattern = "{name} https://example.com/page", RemoveUrls = true };
        Assert.Equal("photo.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);

        Assert.Equal("saved from.jpg",
            PlanSingle("src/saved from www.example.com.jpg", new RenameOptions { RemoveUrls = true }));
    }

    [Fact]
    public void RemoveWords_WholeWordCaseInsensitive_CommaOrSpaceSeparated()
    {
        var o = new RenameOptions { RemoveWords = "copy, final", CollapseSpaces = true };
        Assert.Equal("report Copyright.jpg", PlanSingle("src/report COPY Final Copyright.jpg", o));
    }

    [Fact]
    public void RemoveLeadingAndTrailingNumbers_OnlyTouchEdgeRuns()
    {
        Assert.Equal("shot42end.jpg", PlanSingle("src/007shot42end.jpg",
            new RenameOptions { RemoveLeadingNumbers = true }));
        Assert.Equal("007shot42end.jpg", PlanSingle("src2/007shot42end99.jpg",
            new RenameOptions { RemoveTrailingNumbers = true }));
    }

    [Fact]
    public void RemoveNumbers_OverridesLeadingAndTrailing()
    {
        var o = new RenameOptions { RemoveNumbers = true, RemoveLeadingNumbers = true, RemoveTrailingNumbers = true };
        Assert.Equal("shotend.jpg", PlanSingle("src/007shot42end.jpg", o));
    }

    [Fact]
    public void RemovePunctuation_KeepsDashUnderscoreDotAndSpaces()
    {
        var o = new RenameOptions { RemovePunctuation = true };
        Assert.Equal("a-b_c d.jpg", PlanSingle("src/a-b_c! (d)+#.jpg", o));
    }

    [Fact]
    public void RemoveNonAscii_DropsEverythingAbove7F()
    {
        var o = new RenameOptions { RemoveNonAscii = true };
        Assert.Equal("caf photo.jpg", PlanSingle("src/café photo.jpg", o));
    }

    [Fact]
    public void RemoveEmoji_DropsEmojiButKeepsAccentedText()
    {
        var o = new RenameOptions { RemoveEmoji = true, CollapseSpaces = true };
        Assert.Equal("café sunny.jpg", PlanSingle("src/café 😀☀️ sunny.jpg", o));
    }

    // ---------- inserts ----------

    [Fact]
    public void Insert_BeforeText_AtFirstOccurrence()
    {
        var o = new RenameOptions
        {
            Insert = new InsertRule { Text = "NEW-", Anchor = InsertAnchor.BeforeText, AnchorText = "1234", Enabled = true },
        };
        Assert.Equal("IMG_NEW-1234.jpg", PlanSingle("src/IMG_1234.jpg", o));
    }

    [Fact]
    public void Insert_AfterText()
    {
        var o = new RenameOptions
        {
            Insert = new InsertRule { Text = "-X", Anchor = InsertAnchor.AfterText, AnchorText = "IMG", Enabled = true },
        };
        Assert.Equal("IMG-X_1234.jpg", PlanSingle("src/IMG_1234.jpg", o));
    }

    [Fact]
    public void Insert_AnchorAbsent_IsANoOp()
    {
        var o = new RenameOptions
        {
            Insert = new InsertRule { Text = "X", Anchor = InsertAnchor.BeforeText, AnchorText = "zzz", Enabled = true },
        };
        Assert.Equal("photo.jpg", PlanSingle("src/photo.jpg", o));
    }

    [Fact]
    public void Insert2_RunsAfterInsert()
    {
        var o = new RenameOptions
        {
            Insert = new InsertRule { Text = "A", Position = 0, Enabled = true },
            Insert2 = new InsertRule { Text = "Z", Position = 0, FromEnd = true, Enabled = true },
        };
        Assert.Equal("AphotoZ.jpg", PlanSingle("src/photo.jpg", o));
    }

    // ---------- hygiene ----------

    [Fact]
    public void PadNumberRuns_PadsEveryShortRun_LeavesLongOnes()
    {
        var o = new RenameOptions { PadNumberRunsTo = 3 };
        Assert.Equal("img002take010of5000.jpg", PlanSingle("src/img2take10of5000.jpg", o));
    }

    [Fact]
    public void ReplaceUnderscoresAndDotsWithSpaces_NameOnly()
    {
        var o = new RenameOptions { ReplaceUnderscoresWithSpaces = true, ReplaceDotsWithSpaces = true };
        Assert.Equal("my summer trip v2.jpg", PlanSingle("src/my_summer.trip_v2.jpg", o));
    }

    [Fact]
    public void CollapseRepeatedSeparators_KeepsTheRunsFirstChar()
    {
        var o = new RenameOptions { CollapseRepeatedSeparators = true };
        Assert.Equal("a-b_c d.jpg", PlanSingle("src/a-_ b__c  .d.jpg", o));
    }

    [Fact]
    public void TrimSeparators_TrimsBothEnds()
    {
        var o = new RenameOptions { TrimSeparators = true };
        Assert.Equal("keep me.jpg", PlanSingle("src/-_ keep me _..jpg", o));
    }

    [Fact]
    public void TransliterateToAscii_MapsCommonLettersThenDropsTheRest()
    {
        var o = new RenameOptions { TransliterateToAscii = true };
        Assert.Equal("Strasse-ol-lodz-Thor-AEble.jpg", PlanSingle("src/Straße-øl-łódź-Þor-Æble.jpg", o));
    }

    [Theory]
    [InlineData(TruncateFrom.End, "abcdef")]
    [InlineData(TruncateFrom.Start, "efghij")]
    [InlineData(TruncateFrom.Middle, "abchij")]
    public void MaxNameLength_TruncatesFromTheConfiguredEnd(TruncateFrom from, string expected)
    {
        var o = new RenameOptions { MaxNameLength = 6, TruncateFrom = from };
        Assert.Equal(expected + ".jpg", PlanSingle($"src-{from}/abcdefghij.jpg", o));
    }

    [Fact]
    public void MaxNameLength_OddCap_MiddleKeepsLargerHead()
    {
        var o = new RenameOptions { MaxNameLength = 5, TruncateFrom = TruncateFrom.Middle };
        Assert.Equal("abcij.jpg", PlanSingle("src/abcdefghij.jpg", o));
    }

    // ---------- case transforms ----------

    [Fact]
    public void NameCase_PascalCase()
    {
        var o = new RenameOptions { NameCase = CaseTransform.PascalCase };
        Assert.Equal("MyFileName.jpg", PlanSingle("src/my file name.jpg", o));
    }

    [Fact]
    public void NameCase_CamelCase()
    {
        var o = new RenameOptions { NameCase = CaseTransform.CamelCase };
        Assert.Equal("myFileName.jpg", PlanSingle("src/My File-Name.jpg", o));
    }

    [Fact]
    public void NameCase_SnakeCase()
    {
        var o = new RenameOptions { NameCase = CaseTransform.SnakeCase };
        Assert.Equal("my_file_name.jpg", PlanSingle("src/My File-Name.jpg", o));
    }

    [Fact]
    public void NameCase_KebabCase()
    {
        var o = new RenameOptions { NameCase = CaseTransform.KebabCase };
        Assert.Equal("my-file-name.jpg", PlanSingle("src/My File_Name.jpg", o));
    }

    [Fact]
    public void NameCase_RandomCase_IsDeterministicAcrossPlans()
    {
        var f = _t.File("src/holiday photo album.jpg");
        var o = new RenameOptions { NameCase = CaseTransform.RandomCase };

        var first = Assert.Single(_engine.BuildPlan([f], o)).NewName;
        var second = Assert.Single(_engine.BuildPlan([f], o)).NewName;

        Assert.Equal(first, second); // stable preview
        Assert.Equal("holiday photo album.jpg", first, ignoreCase: true);
    }

    [Fact]
    public void SmartTitleCase_KeepsSmallWordsLowercase_ExceptFirstAndLast()
    {
        var o = new RenameOptions { NameCase = CaseTransform.TitleCase };
        Assert.Equal("The Lord of the Rings.jpg", PlanSingle("src/the lord of the rings.jpg", o));
    }

    [Fact]
    public void SmartTitleCase_LastWordAlwaysCapitalized()
    {
        var o = new RenameOptions { NameCase = CaseTransform.TitleCase };
        Assert.Equal("War of The.jpg", PlanSingle("src/war of the.jpg", o));
    }

    [Fact]
    public void SmartTitleCase_Off_CapitalizesEverything()
    {
        var o = new RenameOptions { NameCase = CaseTransform.TitleCase, SmartTitleCase = false };
        Assert.Equal("The Lord Of The Rings.jpg", PlanSingle("src/the lord of the rings.jpg", o));
    }

    [Fact]
    public void PreserveCaseWords_RestoreTypedCasingAfterTransform()
    {
        var o = new RenameOptions { NameCase = CaseTransform.TitleCase, PreserveCaseWords = "USA iPhone" };
        Assert.Equal("My USA iPhone Pics.jpg", PlanSingle("src/my usa iphone pics.jpg", o));
    }

    [Fact]
    public void PreserveCaseWords_WorkWithLowerTransform()
    {
        var o = new RenameOptions { NameCase = CaseTransform.Lower, PreserveCaseWords = "NASA" };
        Assert.Equal("report for NASA.jpg", PlanSingle("src/REPORT FOR NASA.jpg", o));
    }

    // ---------- affixes ----------

    [Fact]
    public void PrefixOnlyIfMissing_SkipsWhenAlreadyPresent()
    {
        var o = new RenameOptions { Prefix = "IMG_" };
        Assert.Equal("IMG_001.jpg", PlanSingle("src/IMG_001.jpg", o));   // default: only if missing
        Assert.Equal("IMG_photo.jpg", PlanSingle("src/photo.jpg", o));
    }

    [Fact]
    public void Prefix_AlwaysApplied_WhenOnlyIfMissingIsOff()
    {
        var o = new RenameOptions { Prefix = "IMG_", PrefixOnlyIfMissing = false };
        Assert.Equal("IMG_IMG_001.jpg", PlanSingle("src/IMG_001.jpg", o));
    }

    [Fact]
    public void SuffixOnlyIfMissing_SkipsWhenAlreadyPresent()
    {
        var o = new RenameOptions { Suffix = "_edit" };
        Assert.Equal("photo_EDIT.jpg", PlanSingle("src/photo_EDIT.jpg", o)); // ordinal-ignore-case
        Assert.Equal("photo_edit.jpg", PlanSingle("src2/photo.jpg", o));
    }

    [Fact]
    public void ParentFolderAsPrefix_GoesInFrontOfThePrefix()
    {
        var f = _t.File("Holiday 2023/pic.jpg");
        var o = new RenameOptions { Prefix = "X-", ParentFolderAsPrefix = true, ParentPrefixSeparator = " - " };

        Assert.Equal("Holiday 2023 - X-pic.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    // ---------- extension operations ----------

    [Fact]
    public void NewExtension_ReplacesOutright_WithOrWithoutDot()
    {
        Assert.Equal("photo.png", PlanSingle("src/photo.jpg", new RenameOptions { NewExtension = "png" }));
        Assert.Equal("photo.webp", PlanSingle("src2/photo.jpg", new RenameOptions { NewExtension = ".webp" }));
    }

    [Fact]
    public void RemoveExtension_DropsIt_AndBeatsNewExtension()
    {
        var o = new RenameOptions { RemoveExtension = true, NewExtension = "png" };
        Assert.Equal("photo", PlanSingle("src/photo.jpg", o));
    }

    [Theory]
    [InlineData("p.jpeg", "p.jpg")]
    [InlineData("t.tiff", "t.tif")]
    [InlineData("v.mpeg", "v.mpg")]
    [InlineData("h.htm", "h.html")]
    [InlineData("ok.jpg", "ok.jpg")]
    public void NormalizeExtensions_MapsSynonyms(string name, string expected)
    {
        var o = new RenameOptions { NormalizeExtensions = true };
        Assert.Equal(expected, PlanSingle("src/" + name, o));
    }

    [Fact]
    public void Sniffing_CorrectsALyingExtension()
    {
        var f = _t.File("src/actually-png.jpg", MediaFixtures.PngMagic());
        var o = new RenameOptions { FixExtensionBySniffing = true };

        Assert.Equal("actually-png.png", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Theory]
    [InlineData("photo.jpeg")] // synonym spelling of the sniffed type is not "corrected"
    [InlineData("photo.jpg")]
    public void Sniffing_LeavesSynonymSpellingsAlone(string name)
    {
        var f = _t.File("src/" + name, MediaFixtures.JpegMagic());
        var o = new RenameOptions { FixExtensionBySniffing = true };

        Assert.Equal(name, Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void Sniffing_NeverCorrectsTiffBasedRawFiles()
    {
        var f = _t.File("src/raw-shot.cr2", MediaFixtures.TiffMagic());
        var o = new RenameOptions { FixExtensionBySniffing = true };

        Assert.Equal("raw-shot.cr2", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void Sniffing_EbmlPicksMkv_ButWebmStaysWebm()
    {
        var lying = _t.File("src/video.avi", MediaFixtures.EbmlMagic());
        var webm = _t.File("src/clip.webm", MediaFixtures.EbmlMagic());
        var o = new RenameOptions { FixExtensionBySniffing = true };

        var plan = _engine.BuildPlan([lying, webm], o);

        Assert.Equal("video.mkv", NewNameFor(plan, lying));
        Assert.Equal("clip.webm", NewNameFor(plan, webm));
    }

    [Theory]
    [InlineData("mp42", "clip.avi", "clip.mp4")]
    [InlineData("qt  ", "clip.mp4", "clip.mov")]
    [InlineData("heic", "shot.jpg", "shot.heic")]
    [InlineData("M4V ", "clip.m4v", "clip.m4v")]   // m4v is an accepted mp4 spelling
    [InlineData("xxxx", "clip.avi", "clip.avi")]   // unknown brand: not confident, no change
    public void Sniffing_ReadsTheFtypBrand(string brand, string name, string expected)
    {
        var f = _t.File("src/" + name, MediaFixtures.FtypMagic(brand));
        var o = new RenameOptions { FixExtensionBySniffing = true };

        Assert.Equal(expected, Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void Sniffing_RiffAvi()
    {
        var f = _t.File("src/film.mkv", MediaFixtures.RiffMagic("AVI "));
        var o = new RenameOptions { FixExtensionBySniffing = true };

        Assert.Equal("film.avi", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void Sniffing_NewExtensionWins()
    {
        var f = _t.File("src/x.jpg", MediaFixtures.PngMagic());
        var o = new RenameOptions { FixExtensionBySniffing = true, NewExtension = "gif" };

        Assert.Equal("x.gif", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void Sniffing_ThenNormalization_WhenSniffMadeNoCorrection()
    {
        var f = _t.File("src/a.jpeg", MediaFixtures.JpegMagic());
        var o = new RenameOptions { FixExtensionBySniffing = true, NormalizeExtensions = true };

        Assert.Equal("a.jpg", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    // ---------- scope filters ----------

    [Fact]
    public void RegexMasks_MatchAsRegex()
    {
        var img = _t.File("src/IMG_1.jpg");
        var dsc = _t.File("src/DSC_1.jpg");
        var o = new RenameOptions { Pattern = "X_{counter}", IncludeMask = @"^IMG_\d+\.jpg$", UseRegexMasks = true };

        var plan = _engine.BuildPlan([img, dsc], o);

        Assert.Equal(img, Assert.Single(plan).OldPath);
    }

    [Fact]
    public void InvalidRegexMask_KeepsEveryFile_AndFlagsEveryRow()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions { Pattern = "new_{name}", IncludeMask = "[unclosed", UseRegexMasks = true };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, i =>
        {
            Assert.NotNull(i.Problem);
            Assert.StartsWith("[invalid mask]", i.Problem);
        });
    }

    [Fact]
    public void SizeFilters_BoundTheScope()
    {
        var small = _t.File("src/small.jpg", 100);
        var large = _t.File("src/large.jpg", 5000);

        var minPlan = _engine.BuildPlan([small, large],
            new RenameOptions { Pattern = "x_{name}", MinSizeBytes = 1000 });
        Assert.Equal(large, Assert.Single(minPlan).OldPath);

        var maxPlan = _engine.BuildPlan([small, large],
            new RenameOptions { Pattern = "x_{name}", MaxSizeBytes = 1000 });
        Assert.Equal(small, Assert.Single(maxPlan).OldPath);
    }

    [Fact]
    public void ModifiedDateFilters_BoundTheScope()
    {
        var older = _t.File("src/older.jpg");
        var newer = _t.File("src/newer.jpg");
        File.SetLastWriteTime(older, new DateTime(2020, 1, 1));
        File.SetLastWriteTime(newer, new DateTime(2024, 6, 1));

        var afterPlan = _engine.BuildPlan([older, newer],
            new RenameOptions { Pattern = "x_{name}", ModifiedAfter = new DateTime(2022, 1, 1) });
        Assert.Equal(newer, Assert.Single(afterPlan).OldPath);

        var beforePlan = _engine.BuildPlan([older, newer],
            new RenameOptions { Pattern = "x_{name}", ModifiedBefore = new DateTime(2022, 1, 1) });
        Assert.Equal(older, Assert.Single(beforePlan).OldPath);
    }

    [Fact]
    public void SkipHiddenSystem_ExcludesHiddenFiles()
    {
        var visible = _t.File("src/visible.jpg");
        var hidden = _t.File("src/.hidden.jpg");
        try { File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden); }
        catch { /* not settable on this platform */ }
        if ((File.GetAttributes(hidden) & FileAttributes.Hidden) == 0)
            return; // cannot produce a hidden file here; nothing to verify

        var plan = _engine.BuildPlan([visible, hidden], new RenameOptions { Pattern = "x_{name}" });
        Assert.Equal(visible, Assert.Single(plan).OldPath);

        var keepAll = _engine.BuildPlan([visible, hidden],
            new RenameOptions { Pattern = "x_{name}", SkipHiddenSystem = false });
        Assert.Equal(2, keepAll.Count);
    }

    [Fact]
    public void OnlyWithExif_KeepsOnlyFilesWithAMetadataDate()
    {
        var withExif = _t.File("src/real.jpg", MediaFixtures.BuildExifJpeg());
        var without = _t.File("src/garbage.jpg");
        var o = new RenameOptions { Pattern = "x_{name}", OnlyWithExif = true };

        var plan = _engine.BuildPlan([withExif, without], o);

        Assert.Equal(withExif, Assert.Single(plan).OldPath);
    }

    // ---------- collisions & mapping csv ----------

    [Fact]
    public void CollisionSuffixFormat_TemplatesTheAppendedNumber()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions { Pattern = "same", CollisionSuffixFormat = " ({n})" };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Equal(new HashSet<string> { "same.jpg", "same (1).jpg" },
            plan.Select(i => i.NewName).ToHashSet());
    }

    [Fact]
    public void CollisionSuffixFormat_WithoutPlaceholder_GetsTheNumberAppended()
    {
        var a = _t.File("src/a.jpg");
        var b = _t.File("src/b.jpg");
        var o = new RenameOptions { Pattern = "same", CollisionSuffixFormat = "-v" };

        var plan = _engine.BuildPlan([a, b], o);

        Assert.Equal(new HashSet<string> { "same.jpg", "same-v1.jpg" },
            plan.Select(i => i.NewName).ToHashSet());
    }

    [Fact]
    public async Task ExecuteAsync_CollisionAppearingAfterPlanning_RetriesWithTemplate()
    {
        var f = _t.File("src/a.jpg");
        var o = new RenameOptions { Pattern = "target", CollisionSuffixFormat = " ({n})" };
        var plan = _engine.BuildPlan([f], o);
        _t.File("src/target.jpg"); // appears between planning and execution

        var result = await _engine.ExecuteAsync(plan, o);

        Assert.Equal(1, result.Renamed);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(_t.At("src/target (1).jpg")));
    }

    [Fact]
    public async Task ExecuteAsync_CollisionUnderSkipPolicy_SkipsTheFile()
    {
        var f = _t.File("src/a.jpg");
        var o = new RenameOptions { Pattern = "target", ConflictPolicy = RenameConflictPolicy.Skip };
        var plan = _engine.BuildPlan([f], o);
        _t.File("src/target.jpg");

        var result = await _engine.ExecuteAsync(plan, o);

        Assert.Equal(0, result.Renamed);
        Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(f)); // untouched
    }

    [Fact]
    public async Task ExportMappingCsv_WritesQuotedOldNewPairs()
    {
        var a = _t.File("src/with,comma.jpg");
        var b = _t.File("src/plain.jpg");
        var o = new RenameOptions { Pattern = "renamed_{counter}", ExportMappingCsv = true };
        var plan = _engine.BuildPlan([a, b], o);

        var result = await _engine.ExecuteAsync(plan, o);

        Assert.Equal(2, result.Renamed);
        Assert.NotNull(result.MappingCsvPath);
        Assert.True(File.Exists(result.MappingCsvPath));
        Assert.StartsWith(_t.At("src"), result.MappingCsvPath); // next to the first renamed file
        Assert.Matches(@"photon-rename-map-\d{8}-\d{6}\.csv$", Path.GetFileName(result.MappingCsvPath));

        var lines = File.ReadAllLines(result.MappingCsvPath!);
        Assert.Equal("old_path,new_path", lines[0]);
        Assert.Equal(3, lines.Length);
        var commaRow = lines.Skip(1).Select(MiniCsv.ParseLine).Single(f2 => f2[0] == a);
        Assert.Equal(_t.At("src/renamed_001.jpg"), commaRow[1]);
    }

    [Fact]
    public async Task ExportMappingCsv_Off_WritesNothing()
    {
        var f = _t.File("src/a.jpg");
        var o = new RenameOptions { Pattern = "b" };
        var result = await _engine.ExecuteAsync(_engine.BuildPlan([f], o), o);

        Assert.Equal(1, result.Renamed);
        Assert.Null(result.MappingCsvPath);
        Assert.DoesNotContain(Directory.GetFiles(_t.At("src")), p => p.EndsWith(".csv"));
    }
}
