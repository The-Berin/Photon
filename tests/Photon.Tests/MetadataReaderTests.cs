namespace Photon.Tests;

public class MetadataReaderTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly Photon.Core.Services.MetadataReader _reader = TestServices.MetadataReader();

    public void Dispose() => _t.Dispose();

    [Theory]
    [InlineData("garbage.jpg", false)]
    [InlineData("garbage.mp4", true)]
    public void GarbageBytes_NeverThrows_FieldsStayNull(string name, bool isVideo)
    {
        var path = _t.File(name, 4096);   // deterministic random bytes, not a real media file
        var file = Media.File(path, 4096, isVideo: isVideo);
        file.MetadataLoaded = false;

        _reader.Populate(file);           // must not throw

        Assert.True(file.MetadataLoaded);
        Assert.Null(file.ExifDate);
        Assert.Null(file.CameraMake);
        Assert.Null(file.CameraModel);
        Assert.Null(file.GpsLatitude);
        Assert.Null(file.GpsLongitude);
    }

    [Fact]
    public void EmptyFile_NeverThrows()
    {
        var path = _t.File("empty.jpg", 0);
        var file = Media.File(path, 0);
        file.MetadataLoaded = false;

        _reader.Populate(file);

        Assert.True(file.MetadataLoaded);
        Assert.Null(file.ExifDate);
    }

    [Fact]
    public void TruncatedJpegHeader_NeverThrows()
    {
        // A real JPEG SOI marker followed by garbage — parsers must fail gracefully.
        var bytes = new byte[64];
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xE1;
        var path = _t.File("truncated.jpg", bytes);
        var file = Media.File(path, bytes.Length);
        file.MetadataLoaded = false;

        _reader.Populate(file);

        Assert.True(file.MetadataLoaded);
        Assert.Null(file.ExifDate);
        Assert.Null(file.CameraMake);
    }
}
