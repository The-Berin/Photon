using Photon.Core.Services;

namespace Photon.Tests;

/// <summary>The extended MediaFile fields MetadataReader now populates (lens, artist, duration, ...).</summary>
public class MetadataReaderExtendedTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly MetadataReader _reader = TestServices.MetadataReader();

    public void Dispose() => _t.Dispose();

    [Fact]
    public void ExifJpeg_FillsTheExtendedPhotoFields()
    {
        var path = _t.File("shot.jpg", MediaFixtures.BuildExifJpeg());
        var file = Media.File(path);
        file.MetadataLoaded = false;

        _reader.Populate(file);

        Assert.True(file.MetadataLoaded);
        Assert.Equal(MediaFixtures.ExifDate, file.ExifDate);
        Assert.Equal(MediaFixtures.ExifArtist, file.Artist);
        Assert.Equal(MediaFixtures.ExifSoftware, file.Software);
        Assert.Equal(MediaFixtures.ExifOrientation, file.Orientation);
        Assert.Equal(MediaFixtures.ExifLensModel, file.LensModel);
        Assert.NotNull(file.FNumber);
        Assert.Equal(2.8, file.FNumber!.Value, 3);
        Assert.Equal(200, file.IsoSpeed);
        Assert.Equal("1/250", file.ExposureTime); // library renders "1/250 sec"; the suffix is stripped
        Assert.NotNull(file.FocalLengthMm);
        Assert.Equal(50.0, file.FocalLengthMm!.Value, 3);
        Assert.Null(file.DurationSeconds); // stills have no duration
    }

    [Fact]
    public void QuickTimeMov_FillsDurationAndCreatedDate()
    {
        var path = _t.File("clip.mov", MediaFixtures.BuildMov());
        var file = Media.File(path, isVideo: true);
        file.MetadataLoaded = false;

        _reader.Populate(file);

        Assert.True(file.MetadataLoaded);
        Assert.Equal(MediaFixtures.MovCreated, file.ExifDate);
        Assert.NotNull(file.DurationSeconds);
        Assert.Equal(MediaFixtures.MovDurationSeconds, file.DurationSeconds!.Value, 2);
    }

    [Fact]
    public void GarbageBytes_ExtendedFieldsStayNull()
    {
        var path = _t.File("garbage.jpg", 2048);
        var file = Media.File(path);
        file.MetadataLoaded = false;

        _reader.Populate(file);

        Assert.True(file.MetadataLoaded);
        Assert.Null(file.LensModel);
        Assert.Null(file.Artist);
        Assert.Null(file.Software);
        Assert.Null(file.Orientation);
        Assert.Null(file.FNumber);
        Assert.Null(file.IsoSpeed);
        Assert.Null(file.ExposureTime);
        Assert.Null(file.FocalLengthMm);
        Assert.Null(file.DurationSeconds);
    }
}
