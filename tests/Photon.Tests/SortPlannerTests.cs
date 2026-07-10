using Photon.Core.Models;

namespace Photon.Tests;

public class SortPlannerTests : InvariantCultureTest, IDisposable
{
    private readonly TempDir _t = new();
    private readonly Photon.Core.Services.SortPlanner _planner = TestServices.Planner();
    private readonly string _src;
    private readonly string _dest;

    public SortPlannerTests()
    {
        _src = _t.Dir("src");
        _dest = _t.Dir("dest");
    }

    public void Dispose() => _t.Dispose();

    private SortOptions Options(FolderStructure structure = FolderStructure.YearMonthDay,
        MonthFormat month = MonthFormat.Name) => new()
    {
        SourceFolder = _src,
        OutputFolder = _dest,
        Structure = structure,
        MonthFormat = month,
        Action = SortAction.Copy,
    };

    private MediaFile SourceFile(string name = "f.jpg", int size = 100, DateTime? exif = null,
        string? make = null, string? model = null)
        => Media.File(_t.File("src/" + name, size), size, exif: exif ?? Media.DefaultStamp, make: make, model: model);

    // Media.DefaultStamp = 2023-03-05 14:30:22 — March, zero-padded day "05".
    [Theory]
    [InlineData(FolderStructure.YearOnly, MonthFormat.Number, new[] { "2023" })]
    [InlineData(FolderStructure.YearOnly, MonthFormat.Name, new[] { "2023" })]
    [InlineData(FolderStructure.YearMonth, MonthFormat.Number, new[] { "2023", "03" })]
    [InlineData(FolderStructure.YearMonth, MonthFormat.Name, new[] { "2023", "March" })]
    [InlineData(FolderStructure.YearMonthDay, MonthFormat.Number, new[] { "2023", "03", "05" })]
    [InlineData(FolderStructure.YearMonthDay, MonthFormat.Name, new[] { "2023", "March", "05" })]
    public async Task EveryStructureAndMonthFormat_ExactSegments(FolderStructure structure, MonthFormat month, string[] segments)
    {
        var plan = await _planner.BuildPlanAsync([SourceFile()], Options(structure, month));

        var expected = Path.Combine([_dest, .. segments, "f.jpg"]);
        Assert.Equal(expected, Assert.Single(plan.Items).PlannedDestination);
    }

    [Fact]
    public async Task ResolvedDate_AndExifFlag_Recorded()
    {
        var plan = await _planner.BuildPlanAsync([SourceFile()], Options());
        var item = Assert.Single(plan.Items);
        Assert.Equal(Media.DefaultStamp, item.ResolvedDate);
        Assert.True(item.DateFromExif);
    }

    [Fact]
    public async Task TimeSubfolder_AddsHHdashMMUnderDayFolder()
    {
        var o = Options();
        o.IncludeTimeSubfolder = true;

        var plan = await _planner.BuildPlanAsync([SourceFile()], o);

        var expected = Path.Combine(_dest, "2023", "March", "05", "14-30", "f.jpg");
        Assert.Equal(expected, Assert.Single(plan.Items).PlannedDestination);
    }

    [Fact]
    public async Task CameraGrouping_MakeAndModelFolders()
    {
        var o = Options();
        o.GroupByCamera = true;

        var plan = await _planner.BuildPlanAsync([SourceFile(make: "Sony", model: "A7 III")], o);

        var expected = Path.Combine(_dest, "2023", "March", "05", "Sony", "A7 III", "f.jpg");
        Assert.Equal(expected, Assert.Single(plan.Items).PlannedDestination);
    }

    [Fact]
    public async Task CameraGrouping_SanitizesSlashes()
    {
        var o = Options();
        o.GroupByCamera = true;

        var plan = await _planner.BuildPlanAsync([SourceFile(make: "NIKON/CORP", model: "D750\\x")], o);

        var dest = Assert.Single(plan.Items).PlannedDestination;
        var relative = Path.GetRelativePath(_dest, dest);
        var segments = relative.Split(Path.DirectorySeparatorChar);
        Assert.Contains("NIKONCORP", segments);
        Assert.Contains("D750x", segments);
        // No segment may retain a path separator or invalid char.
        Assert.All(segments, s => Assert.DoesNotContain('/', s));
    }

    [Fact]
    public async Task CameraGrouping_MissingCamera_UnknownCameraFolder()
    {
        var o = Options();
        o.GroupByCamera = true;

        var plan = await _planner.BuildPlanAsync([SourceFile()], o);

        var dest = Assert.Single(plan.Items).PlannedDestination;
        var segments = Path.GetRelativePath(_dest, dest).Split(Path.DirectorySeparatorChar);
        Assert.Contains("Unknown Camera", segments);
    }

    [Fact]
    public async Task NoResolvableDate_GoesToUnknownDateFolder()
    {
        var o = Options();
        o.DateSource = DateSource.ExifOnly;
        o.UnknownDateFolderName = "No Date Here";
        var file = Media.File(_t.File("src/nodate.jpg", 50), 50); // no exif

        var plan = await _planner.BuildPlanAsync([file], o);

        var item = Assert.Single(plan.Items);
        Assert.Equal(Path.Combine(_dest, "No Date Here", "nodate.jpg"), item.PlannedDestination);
        Assert.Null(item.ResolvedDate);
    }

    [Fact]
    public async Task FilesAlreadyUnderDestinationRoot_ExcludedAndWarned()
    {
        var inside = Media.File(_t.File("dest/already/sorted.jpg", 60), 60, exif: Media.DefaultStamp);
        var outside = SourceFile();

        var plan = await _planner.BuildPlanAsync([outside, inside], Options());

        Assert.Single(plan.Items);
        Assert.Equal(outside.FilePath, plan.Items[0].Source.FilePath);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public async Task OutputEqualsSource_SortsInPlace_NothingExcluded()
    {
        // Organize-in-place: the re-sort exclusion would otherwise exclude every file.
        var o = Options();
        o.OutputFolder = _src;

        var plan = await _planner.BuildPlanAsync([SourceFile("a.jpg"), SourceFile("b.jpg")], o);

        Assert.Equal(2, plan.Items.Count);
    }

    [Fact]
    public async Task OutputIsAncestorOfSource_NothingExcluded()
    {
        var o = Options();
        o.OutputFolder = _t.Root;   // ancestor of the source folder

        var plan = await _planner.BuildPlanAsync([SourceFile("a.jpg")], o);

        Assert.Single(plan.Items);
    }

    [Fact]
    public async Task ExportCsvSummary_ForcesMetadataRead_UnderFileDateOnly()
    {
        // The CSV promises camera/gps columns; "File date only" alone skips metadata.
        var recorder = new RecordingMetadataReader();
        var planner = new Photon.Core.Services.SortPlanner(recorder, TestServices.DateResolver());
        var file = new MediaFile
        {
            FilePath = _t.File("src/f.jpg", 100),
            SizeBytes = 100,
            FileCreated = Media.DefaultStamp,
            FileModified = Media.DefaultStamp,
        };
        var o = Options();
        o.DateSource = DateSource.FileDateOnly;
        o.ExportCsvSummary = true;

        await planner.BuildPlanAsync([file], o);

        Assert.Equal(1, recorder.Calls);
    }

    private sealed class RecordingMetadataReader : Photon.Core.Services.IMetadataReader
    {
        public int Calls;
        public void Populate(MediaFile file)
        {
            Calls++;
            file.MetadataLoaded = true;
        }
    }

    [Fact]
    public async Task RequiredBytes_CopySumsAllFiles()
    {
        var files = new[] { SourceFile("a.jpg", 100), SourceFile("b.jpg", 250) };
        var plan = await _planner.BuildPlanAsync(files, Options());

        Assert.Equal(350, plan.TotalBytes);
        Assert.Equal(350, plan.RequiredBytes);
        Assert.True(plan.DestinationFreeBytes > 0);
    }

    [Fact]
    public async Task RequiredBytes_SameVolumeMove_IsZero()
    {
        // Source and destination both live under the same temp root — same volume.
        var o = Options();
        o.Action = SortAction.Move;

        var plan = await _planner.BuildPlanAsync([SourceFile("a.jpg", 100), SourceFile("b.jpg", 250)], o);

        Assert.Equal(350, plan.TotalBytes);
        Assert.Equal(0, plan.RequiredBytes);
    }

    [Fact]
    public async Task DestinationRoot_MatchesResolvedOutputFolder()
    {
        var plan = await _planner.BuildPlanAsync([SourceFile()], Options());
        Assert.Equal(_dest, plan.DestinationRoot);
    }

    [Fact]
    public void HasEnoughSpace_HonorsSafetyMargin()
    {
        var plan = new SortPlan
        {
            DestinationRoot = _dest,
            RequiredBytes = 1000,
            DestinationFreeBytes = 1000 + SortPlan.SafetyMarginBytes - 1,
        };
        Assert.False(plan.HasEnoughSpace);

        var ok = new SortPlan
        {
            DestinationRoot = _dest,
            RequiredBytes = 1000,
            DestinationFreeBytes = 1000 + SortPlan.SafetyMarginBytes,
        };
        Assert.True(ok.HasEnoughSpace);
    }
}
