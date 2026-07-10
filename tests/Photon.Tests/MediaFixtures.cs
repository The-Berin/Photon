using System.Text;
using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.Tests;

/// <summary>
/// Hand-built media file fixtures: a minimal JPEG with a real EXIF block, a minimal
/// QuickTime .mov with an mvhd atom, and bare magic-byte headers for sniffing tests.
/// Both containers were validated against MetadataExtractor 2.8.1's parsers.
/// </summary>
internal static class MediaFixtures
{
    // The known values baked into BuildExifJpeg.
    public const string ExifArtist = "Baron Gartner";
    public const string ExifSoftware = "Photon Test 1.0";
    public const int ExifOrientation = 6;
    public const string ExifLensModel = "EF50mm f/1.8 STM";
    public static readonly DateTime ExifDate = new(2023, 3, 5, 14, 30, 22);

    // The known values baked into BuildMov.
    public const double MovDurationSeconds = 155;
    public static readonly DateTime MovCreated = new(2023, 3, 5, 14, 30, 22);

    /// <summary>
    /// SOI + APP1(Exif) + EOI. IFD0: Orientation 6, Artist, Software. ExifSubIFD:
    /// DateTimeOriginal 2023-03-05 14:30:22, ExposureTime 1/250, FNumber f/2.8,
    /// ISO 200, FocalLength 50 mm, LensModel.
    /// </summary>
    public static byte[] BuildExifJpeg()
    {
        List<(ushort Tag, ushort Type, uint Count, byte[]? Inline, byte[]? Data)> ifd0 = [], sub = [];

        AddShort(ifd0, 0x0112, ExifOrientation);
        AddAscii(ifd0, 0x013B, ExifArtist);
        AddAscii(ifd0, 0x0131, ExifSoftware);
        AddAscii(sub, 0x9003, "2023:03:05 14:30:22"); // DateTimeOriginal
        AddRational(sub, 0x829A, 1, 250);             // ExposureTime
        AddRational(sub, 0x829D, 28, 10);             // FNumber
        AddShort(sub, 0x8827, 200);                   // ISO
        AddRational(sub, 0x920A, 50, 1);              // FocalLength
        AddAscii(sub, 0xA434, ExifLensModel);

        // Layout: TIFF header(8), IFD0 (with an extra ExifOffset pointer entry), sub-IFD, data area.
        int ifd0Entries = ifd0.Count + 1;
        uint subOffset = (uint)(8 + 2 + 12 * ifd0Entries + 4);
        uint dataOffset = (uint)(subOffset + 2 + 12 * sub.Count + 4);

        var tiff = new List<byte> { 0x49, 0x49, 0x2A, 0x00 }; // "II" little-endian
        tiff.AddRange(BitConverter.GetBytes(8u));             // IFD0 offset
        var data = new List<byte>();
        WriteIfd(tiff, data, ifd0, dataOffset, subOffset);
        WriteIfd(tiff, data, sub, dataOffset, exifSubIfdOffset: null);
        tiff.AddRange(data);

        var app1 = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
        var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1 }; // SOI + APP1 marker
        int segmentLength = app1.Length + 2;
        jpeg.Add((byte)(segmentLength >> 8));
        jpeg.Add((byte)segmentLength);
        jpeg.AddRange(app1);
        jpeg.AddRange([0xFF, 0xD9]); // EOI
        return [.. jpeg];
    }

    /// <summary>ftyp("qt  ") + moov/mvhd: created/modified 2023-03-05 14:30:22, 155 s duration.</summary>
    public static byte[] BuildMov()
    {
        var b = new List<byte>();
        WriteU32(b, 20); WriteAscii(b, "ftyp"); WriteAscii(b, "qt  "); WriteU32(b, 0); WriteAscii(b, "qt  ");
        WriteU32(b, 116); WriteAscii(b, "moov");
        WriteU32(b, 108); WriteAscii(b, "mvhd");
        b.AddRange([0, 0, 0, 0]); // version + flags
        uint created = (uint)(MovCreated - new DateTime(1904, 1, 1)).TotalSeconds;
        WriteU32(b, created);                          // created
        WriteU32(b, created);                          // modified
        WriteU32(b, 600);                              // timescale
        WriteU32(b, (uint)(600 * MovDurationSeconds)); // duration in timescale units
        WriteU32(b, 0x00010000);                       // preferred rate 1.0
        b.AddRange([0x01, 0x00]);                      // preferred volume 1.0
        b.AddRange(new byte[10]);                      // reserved
        foreach (var m in new uint[] { 0x00010000, 0, 0, 0, 0x00010000, 0, 0, 0, 0x40000000 })
            WriteU32(b, m);                            // identity matrix
        b.AddRange(new byte[24]);                      // preview/poster/selection/current times
        WriteU32(b, 2);                                // next track id
        return [.. b];
    }

    // ----- bare magic-byte headers (padded to 16 bytes) -----

    public static byte[] JpegMagic() => Pad([0xFF, 0xD8, 0xFF, 0xE0]);
    public static byte[] PngMagic() => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    public static byte[] TiffMagic() => Pad([0x49, 0x49, 0x2A, 0x00]);
    public static byte[] EbmlMagic() => Pad([0x1A, 0x45, 0xDF, 0xA3]);
    public static byte[] GifMagic() => Pad(Encoding.ASCII.GetBytes("GIF89a"));

    public static byte[] FtypMagic(string brand)
    {
        var b = new List<byte>();
        WriteU32(b, 16);
        WriteAscii(b, "ftyp");
        WriteAscii(b, brand);
        WriteU32(b, 0);
        return [.. b];
    }

    public static byte[] RiffMagic(string form)
    {
        var b = new List<byte>();
        WriteAscii(b, "RIFF");
        WriteU32(b, 8);
        WriteAscii(b, form);
        WriteU32(b, 0);
        return [.. b];
    }

    // ----- helpers -----

    private static byte[] Pad(byte[] header)
    {
        var padded = new byte[Math.Max(16, header.Length)];
        header.CopyTo(padded, 0);
        return padded;
    }

    private static void WriteU32(List<byte> b, uint v)
    {
        b.Add((byte)(v >> 24));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static void WriteAscii(List<byte> b, string s)
    {
        foreach (var c in s) b.Add((byte)c);
    }

    private static void AddAscii(List<(ushort, ushort, uint, byte[]?, byte[]?)> ifd, ushort tag, string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s + "\0");
        ifd.Add((tag, 2, (uint)bytes.Length, null, bytes));
    }

    private static void AddShort(List<(ushort, ushort, uint, byte[]?, byte[]?)> ifd, ushort tag, int value)
    {
        var inline = new byte[4];
        BitConverter.GetBytes((ushort)value).CopyTo(inline, 0);
        ifd.Add((tag, 3, 1, inline, null));
    }

    private static void AddRational(List<(ushort, ushort, uint, byte[]?, byte[]?)> ifd, ushort tag, uint num, uint den)
    {
        var bytes = new byte[8];
        BitConverter.GetBytes(num).CopyTo(bytes, 0);
        BitConverter.GetBytes(den).CopyTo(bytes, 4);
        ifd.Add((tag, 5, 1, null, bytes));
    }

    private static void WriteIfd(List<byte> tiff, List<byte> data,
        List<(ushort Tag, ushort Type, uint Count, byte[]? Inline, byte[]? Data)> entries,
        uint dataOffset, uint? exifSubIfdOffset)
    {
        var ordered = entries.OrderBy(e => e.Tag).ToList();
        int count = ordered.Count + (exifSubIfdOffset is null ? 0 : 1);
        tiff.AddRange(BitConverter.GetBytes((ushort)count));
        bool pointerWritten = false;

        void WritePointer()
        {
            tiff.AddRange(BitConverter.GetBytes((ushort)0x8769)); // ExifOffset
            tiff.AddRange(BitConverter.GetBytes((ushort)4));      // LONG
            tiff.AddRange(BitConverter.GetBytes(1u));
            tiff.AddRange(BitConverter.GetBytes(exifSubIfdOffset!.Value));
            pointerWritten = true;
        }

        foreach (var e in ordered)
        {
            if (exifSubIfdOffset is not null && !pointerWritten && e.Tag > 0x8769) WritePointer();
            tiff.AddRange(BitConverter.GetBytes(e.Tag));
            tiff.AddRange(BitConverter.GetBytes(e.Type));
            tiff.AddRange(BitConverter.GetBytes(e.Count));
            if (e.Inline is not null)
            {
                tiff.AddRange(e.Inline);
            }
            else if (e.Data!.Length <= 4)
            {
                var inline = new byte[4];
                e.Data.CopyTo(inline, 0);
                tiff.AddRange(inline);
            }
            else
            {
                tiff.AddRange(BitConverter.GetBytes((uint)(dataOffset + data.Count)));
                data.AddRange(e.Data);
            }
        }
        if (exifSubIfdOffset is not null && !pointerWritten) WritePointer();
        tiff.AddRange(BitConverter.GetBytes(0u)); // next IFD
    }
}

/// <summary>IMetadataReader stub: fills whatever the test wants, never reads the file.</summary>
internal sealed class StubMetadataReader(Action<MediaFile>? fill = null) : IMetadataReader
{
    public void Populate(MediaFile file)
    {
        fill?.Invoke(file);
        file.MetadataLoaded = true;
    }
}
