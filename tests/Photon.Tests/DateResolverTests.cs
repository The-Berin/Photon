using Photon.Core.Models;

namespace Photon.Tests;

public class DateResolverTests
{
    private static readonly DateTime Created = new(2021, 1, 2, 10, 0, 0);
    private static readonly DateTime Modified = new(2020, 6, 7, 8, 0, 0);   // earlier than Created
    private static readonly DateTime Exif = new(2019, 3, 4, 12, 34, 56);

    private static MediaFile WithExif() =>
        Media.File("/x/a.jpg", created: Created, modified: Modified, exif: Exif);

    private static MediaFile WithoutExif() =>
        Media.File("/x/a.jpg", created: Created, modified: Modified);

    private readonly Photon.Core.Services.DateResolver _resolver = TestServices.DateResolver();

    // File date is always min(created, modified) — here Modified is the earlier one.
    private static readonly DateTime FileDate = Modified;

    [Fact]
    public void ExifThenFileDate_ExifPresent_UsesExif()
    {
        var r = _resolver.Resolve(WithExif(), DateSource.ExifThenFileDate);
        Assert.Equal(Exif, r.Date);
        Assert.True(r.FromExif);
    }

    [Fact]
    public void ExifThenFileDate_ExifAbsent_FallsBackToFileDate()
    {
        var r = _resolver.Resolve(WithoutExif(), DateSource.ExifThenFileDate);
        Assert.Equal(FileDate, r.Date);
        Assert.False(r.FromExif);
    }

    [Fact]
    public void ExifOnly_ExifPresent_UsesExif()
    {
        var r = _resolver.Resolve(WithExif(), DateSource.ExifOnly);
        Assert.Equal(Exif, r.Date);
        Assert.True(r.FromExif);
    }

    [Fact]
    public void ExifOnly_ExifAbsent_ReturnsNull()
    {
        var r = _resolver.Resolve(WithoutExif(), DateSource.ExifOnly);
        Assert.Null(r.Date);
        Assert.False(r.FromExif);
    }

    [Fact]
    public void FileDateThenExif_UsesFileDateEvenWhenExifPresent()
    {
        var r = _resolver.Resolve(WithExif(), DateSource.FileDateThenExif);
        Assert.Equal(FileDate, r.Date);
        Assert.False(r.FromExif);
    }

    [Fact]
    public void FileDateThenExif_ExifAbsent_UsesFileDate()
    {
        var r = _resolver.Resolve(WithoutExif(), DateSource.FileDateThenExif);
        Assert.Equal(FileDate, r.Date);
        Assert.False(r.FromExif);
    }

    [Fact]
    public void FileDateOnly_IgnoresExif()
    {
        var r = _resolver.Resolve(WithExif(), DateSource.FileDateOnly);
        Assert.Equal(FileDate, r.Date);
        Assert.False(r.FromExif);
    }

    [Fact]
    public void FileDate_IsMinOfCreatedAndModified_EitherOrder()
    {
        // created earlier than modified
        var a = Media.File("/x/a.jpg", created: new DateTime(2018, 5, 1), modified: new DateTime(2022, 5, 1));
        Assert.Equal(new DateTime(2018, 5, 1), _resolver.Resolve(a, DateSource.FileDateOnly).Date);

        // modified earlier than created (typical for copied files)
        var b = Media.File("/x/b.jpg", created: new DateTime(2022, 5, 1), modified: new DateTime(2018, 5, 1));
        Assert.Equal(new DateTime(2018, 5, 1), _resolver.Resolve(b, DateSource.FileDateOnly).Date);
    }
}
