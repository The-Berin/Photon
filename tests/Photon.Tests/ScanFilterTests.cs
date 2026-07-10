using Photon.Core.Models;

namespace Photon.Tests;

public class ScanFilterTests
{
    private static SortOptions Options(string custom = "", bool pictures = true, bool videos = true, bool subfolders = true)
        => new() { CustomExtensions = custom, IncludePictures = pictures, IncludeVideos = videos, IncludeSubfolders = subfolders };

    [Theory]
    [InlineData("jpg png raw")]
    [InlineData("jpg,png,raw")]
    [InlineData("jpg;png;raw")]
    [InlineData("jpg, png; raw")]
    [InlineData("  jpg   png ,, raw ;")]
    public void FromSortOptions_CustomExtensions_AllSeparators(string custom)
    {
        var f = ScanFilter.FromSortOptions(Options(custom));
        Assert.Equal(new HashSet<string> { ".jpg", ".png", ".raw" }, f.PictureExtensions);
        Assert.Empty(f.VideoExtensions);
    }

    [Fact]
    public void FromSortOptions_CustomExtensions_LeadingDotsOptionalAndMixedCase()
    {
        var f = ScanFilter.FromSortOptions(Options(".JPG png .Heic RAW"));
        Assert.Equal(new HashSet<string> { ".jpg", ".png", ".heic", ".raw" }, f.PictureExtensions);
    }

    [Theory]
    [InlineData("*.jpg", ".jpg")]     // Explorer-style glob
    [InlineData("jpg.", ".jpg")]      // stray trailing dot
    [InlineData(".jpg.", ".jpg")]
    [InlineData("tar.gz", ".gz")]     // only the final extension segment is matchable
    public void FromSortOptions_CustomExtensions_SanitizesGlobsAndDots(string custom, string expected)
    {
        var f = ScanFilter.FromSortOptions(Options(custom));
        Assert.Equal(new HashSet<string> { expected }, f.PictureExtensions);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("* . .. ***")]
    public void FromSortOptions_CustomExtensions_OnlyGlobs_FallBackToTypeFlags(string custom)
    {
        var f = ScanFilter.FromSortOptions(Options(custom));
        Assert.Equal(ScanFilter.DefaultPictureExtensions.ToHashSet(), f.PictureExtensions);
        Assert.Equal(ScanFilter.DefaultVideoExtensions.ToHashSet(), f.VideoExtensions);
    }

    [Fact]
    public void FromSortOptions_CustomVideoExtensions_KeepVideoClassification()
    {
        // Video containers must stay classified as video so their dates come from the
        // movie header, not (absent) EXIF IFDs.
        var f = ScanFilter.FromSortOptions(Options("mp4 jpg MOV"));
        Assert.Equal(new HashSet<string> { ".jpg" }, f.PictureExtensions);
        Assert.Equal(new HashSet<string> { ".mp4", ".mov" }, f.VideoExtensions);
    }

    [Fact]
    public void FromSortOptions_CustomOverridesTypeFlags()
    {
        // Both flags off, but a custom list still wins.
        var f = ScanFilter.FromSortOptions(Options("tif", pictures: false, videos: false));
        Assert.Equal(new HashSet<string> { ".tif" }, f.PictureExtensions);
        Assert.Empty(f.VideoExtensions);
    }

    [Fact]
    public void FromSortOptions_FlagsOff_EmptySets()
    {
        var f = ScanFilter.FromSortOptions(Options(pictures: false, videos: false));
        Assert.Empty(f.PictureExtensions);
        Assert.Empty(f.VideoExtensions);
    }

    [Fact]
    public void FromSortOptions_PicturesOnly()
    {
        var f = ScanFilter.FromSortOptions(Options(videos: false));
        Assert.Equal(ScanFilter.DefaultPictureExtensions.ToHashSet(), f.PictureExtensions);
        Assert.Empty(f.VideoExtensions);
    }

    [Fact]
    public void FromSortOptions_VideosOnly()
    {
        var f = ScanFilter.FromSortOptions(Options(pictures: false));
        Assert.Empty(f.PictureExtensions);
        Assert.Equal(ScanFilter.DefaultVideoExtensions.ToHashSet(), f.VideoExtensions);
    }

    [Fact]
    public void FromSortOptions_BothFlags_DefaultSets()
    {
        var f = ScanFilter.FromSortOptions(Options());
        Assert.Equal(ScanFilter.DefaultPictureExtensions.ToHashSet(), f.PictureExtensions);
        Assert.Equal(ScanFilter.DefaultVideoExtensions.ToHashSet(), f.VideoExtensions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromSortOptions_RecursiveFollowsIncludeSubfolders(bool subfolders)
    {
        Assert.Equal(subfolders, ScanFilter.FromSortOptions(Options(subfolders: subfolders)).Recursive);
        Assert.Equal(subfolders, ScanFilter.FromSortOptions(Options("jpg", subfolders: subfolders)).Recursive);
    }
}
