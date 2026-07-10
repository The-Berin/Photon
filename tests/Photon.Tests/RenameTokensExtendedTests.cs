using System.Globalization;
using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

/// <summary>
/// The new pattern tokens: calendar parts, file identity ({drive}, {depth}, ...),
/// extended EXIF/video/GPS fields (via a stub metadata reader) and the content hashes.
/// </summary>
public class RenameTokensExtendedTests : InvariantCultureTest, IDisposable
{
    private static readonly DateTime Stamp = new(2020, 5, 6, 7, 8, 9); // a Wednesday, ISO week 19

    private readonly TempDir _t = new();
    private readonly JournalService _journal;
    private readonly RenameEngine _engine;

    public RenameTokensExtendedTests()
    {
        _journal = TestServices.Journal(_t.Dir("journals"));
        _engine = TestServices.RenameEngine(_journal);
    }

    public void Dispose() => _t.Dispose();

    private RenameEngine EngineWith(Action<MediaFile> fill) =>
        new(_journal, new StubMetadataReader(fill), new DateResolver());

    private string PlanName(RenameEngine engine, string path, string pattern, DateSource? source = null)
    {
        var o = new RenameOptions { Pattern = pattern };
        if (source is DateSource s) o.DateSource = s;
        return Assert.Single(engine.BuildPlan([path], o)).NewName;
    }

    private string DatedPlanName(string pattern)
    {
        var f = _t.File("src/pic.jpg");
        File.SetLastWriteTime(f, Stamp); // min(created, modified) = this mtime
        return PlanName(_engine, f, pattern, DateSource.FileDateOnly);
    }

    // ---------- calendar tokens ----------

    [Theory]
    [InlineData("{week}", "19")]           // ISO-8601 week
    [InlineData("{quarter}", "Q2")]
    [InlineData("{dayofyear}", "127")]     // 2020 is a leap year
    [InlineData("{weekday}", "Wednesday")]
    [InlineData("{hh12}", "07")]
    [InlineData("{ampm}", "AM")]
    public void CalendarTokens_DeriveFromTheResolvedDate(string pattern, string expected)
    {
        Assert.Equal(expected + ".jpg", DatedPlanName(pattern));
    }

    [Fact]
    public void Hh12AndAmPm_Afternoon()
    {
        var f = _t.File("src/pm.jpg");
        File.SetLastWriteTime(f, new DateTime(2020, 5, 6, 14, 30, 0));

        Assert.Equal("02 PM.jpg", PlanName(_engine, f, "{hh12} {ampm}", DateSource.FileDateOnly));
    }

    [Fact]
    public void EpochToken_IsTheUtcUnixSecondsOfTheResolvedDate()
    {
        var expected = new DateTimeOffset(Stamp, TimeSpan.Zero).ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        Assert.Equal(expected + ".jpg", DatedPlanName("{epoch}"));
    }

    [Fact]
    public void AgeDaysToken_CountsWholeDaysSinceTheResolvedDate()
    {
        var name = DatedPlanName("{age-days}");
        var days = int.Parse(Path.GetFileNameWithoutExtension(name), CultureInfo.InvariantCulture);
        var expected = (int)(DateTime.Now - Stamp).TotalDays;

        Assert.InRange(days, expected - 1, expected + 1);
    }

    // ---------- file identity tokens ----------

    [Fact]
    public void OrigExtToken_IsTheOriginalExtensionEvenWhenReplaced()
    {
        var f = _t.File("src/photo.jpg");
        var o = new RenameOptions { Pattern = "{name}-{origext}", NewExtension = "png" };

        Assert.Equal("photo-jpg.png", Assert.Single(_engine.BuildPlan([f], o)).NewName);
    }

    [Fact]
    public void Parent3Token_WalksThreeLevelsUp()
    {
        var f = _t.File("one/two/three/pic.jpg");
        Assert.Equal("one_pic.jpg", PlanName(_engine, f, "{parent3}_{name}"));
    }

    [Fact]
    public void DriveToken_IsTheTrimmedPathRoot()
    {
        var f = _t.File("src/pic.jpg");
        var root = Path.GetPathRoot(Path.GetFullPath(f))!;
        var expected = root.TrimEnd('\\', '/').TrimEnd(':');

        Assert.Equal("d" + expected + ".jpg", PlanName(_engine, f, "d{drive}"));
    }

    [Fact]
    public void DepthToken_CountsDirectoryLevelsBelowTheRoot()
    {
        var f = _t.File("a/b/pic.jpg");
        var dir = Path.GetDirectoryName(Path.GetFullPath(f))!;
        var root = Path.GetPathRoot(dir)!;
        var expected = dir[root.Length..]
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Length
            .ToString(CultureInfo.InvariantCulture);

        Assert.Equal(expected + ".jpg", PlanName(_engine, f, "{depth}"));
    }

    [Fact]
    public void FileSizeBytesToken_IsTheExactByteCount()
    {
        var f = _t.File("src/pic.jpg", 2048);
        Assert.Equal("2048.jpg", PlanName(_engine, f, "{filesize-bytes}"));
    }

    // ---------- EXIF / video / GPS tokens ----------

    [Fact]
    public void ExifTokens_RenderTheExtendedMetadataFields()
    {
        var engine = EngineWith(m =>
        {
            m.LensModel = "EF 50mm";
            m.Artist = "Ana";
            m.Software = "Darktable";
            m.Orientation = 6;
            m.FNumber = 2.8;
            m.IsoSpeed = 200;
            m.ExposureTime = "1/250";
            m.FocalLengthMm = 49.7;
        });
        var f = _t.File("src/pic.jpg");

        Assert.Equal("EF 50mm_Ana_Darktable_6_f2.8_200_1-250_50.jpg",
            PlanName(engine, f, "{lens}_{artist}_{software}_{orientation}_{fnumber}_{iso-speed}_{exposure}_{focal}"));
    }

    [Fact]
    public void FNumberToken_WholeNumbersLoseTheDecimal()
    {
        var engine = EngineWith(m => m.FNumber = 11.0);
        var f = _t.File("src/pic.jpg");

        Assert.Equal("f11.jpg", PlanName(engine, f, "{fnumber}"));
    }

    [Fact]
    public void DurationTokens_MinutesDotSeconds_AndWholeSeconds()
    {
        var engine = EngineWith(m => m.DurationSeconds = 155.2);
        var f = _t.File("src/clip.mp4");

        Assert.Equal("2.35_155.mp4", PlanName(engine, f, "{duration}_{duration-s}"));
    }

    [Fact]
    public void DurationToken_RoundingCarriesIntoTheMinute()
    {
        var engine = EngineWith(m => m.DurationSeconds = 59.6);
        var f = _t.File("src/clip.mp4");

        Assert.Equal("1.00_60.mp4", PlanName(engine, f, "{duration}_{duration-s}"));
    }

    [Fact]
    public void GpsTokens_FiveDecimals_MinusAllowed()
    {
        var engine = EngineWith(m =>
        {
            m.GpsLatitude = 51.5074;
            m.GpsLongitude = -0.1278;
        });
        var f = _t.File("src/pic.jpg");

        Assert.Equal("51.50740_-0.12780.jpg", PlanName(engine, f, "{lat}_{lon}"));
        Assert.Equal("51.50740,-0.12780.jpg", PlanName(engine, f, "{gps}"));
    }

    [Fact]
    public void MetadataTokens_NullFieldsBecomeEmptyStrings()
    {
        var engine = EngineWith(_ => { }); // loads, fills nothing
        var f = _t.File("src/pic.jpg");

        Assert.Equal("x.jpg", PlanName(engine, f, "{lens}{artist}{fnumber}{gps}{duration}{orientation}x"));
    }

    // ---------- content hashes ----------

    [Fact]
    public void HashTokens_KnownContent_KnownDigests()
    {
        var f = _t.TextFile("src/hello.jpg", "hello");

        // md5("hello") = 5d41402a..., sha1("hello") = aaf4c61d..., crc32("hello") = 3610a686
        Assert.Equal("5d41402a_aaf4c61d_3610a686.jpg", PlanName(_engine, f, "{md5-8}_{sha1-8}_{crc32}"));
    }

    [Fact]
    public void Crc32Token_MatchesTheStandardCheckValue()
    {
        var f = _t.TextFile("src/check.jpg", "123456789");
        Assert.Equal("cbf43926.jpg", PlanName(_engine, f, "{crc32}")); // CRC-32 check value
    }

    // ---------- token grammar ----------

    [Fact]
    public void UnknownHyphenatedTokens_StayVerbatim()
    {
        var f = _t.File("src/photo.jpg");
        Assert.Equal("photo{bogus-token}.jpg", PlanName(_engine, f, "{name}{bogus-token}"));
    }
}
