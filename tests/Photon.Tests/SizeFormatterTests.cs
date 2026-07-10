using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.Tests;

public class SizeFormatterTests : InvariantCultureTest
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(1_234_567, "1,234,567 B")]
    public void FixedBytes(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes, SizeUnit.Bytes));

    [Theory]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(512, "0.50 KB")]
    public void FixedKB(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes, SizeUnit.KB));

    [Theory]
    [InlineData(1_048_576, "1.00 MB")]
    [InlineData(123_456_789, "117.74 MB")]
    public void FixedMB(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes, SizeUnit.MB));

    [Fact]
    public void FixedGB() => Assert.Equal("1.00 GB", SizeFormatter.Format(1L << 30, SizeUnit.GB));

    [Fact]
    public void FixedTB() => Assert.Equal("1.00 TB", SizeFormatter.Format(1L << 40, SizeUnit.TB));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1023, "1,023 B")]          // still bytes just below the boundary
    [InlineData(1024, "1.00 KB")]          // rolls over exactly at 1024
    [InlineData(1536, "1.50 KB")]
    [InlineData(1_048_575, "1,024.00 KB")] // one byte below the MB boundary stays KB
    [InlineData(1_048_576, "1.00 MB")]
    [InlineData(1_073_741_824, "1.00 GB")]
    [InlineData(5_368_709_120, "5.00 GB")]
    public void Auto_RollsOverAt1024Boundaries(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Fact]
    public void Auto_PetabyteIsTopUnit()
        => Assert.Equal("1.00 PB", SizeFormatter.Format(1L << 50));

    [Theory]
    [InlineData(-1, "-1 B")]
    [InlineData(-1024, "-1.00 KB")]
    [InlineData(-1_048_576, "-1.00 MB")]
    public void Auto_NegativeValues(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Fact]
    public void Fixed_NegativeValues()
        => Assert.Equal("-2.00 KB", SizeFormatter.Format(-2048, SizeUnit.KB));

    [Theory]
    [InlineData(0d, "0 B/s")]
    [InlineData(500d, "500 B/s")]
    [InlineData(1024d, "1.00 KB/s")]
    [InlineData(2048.9, "2.00 KB/s")]       // fractional rates truncate to whole bytes
    [InlineData(1_048_576d, "1.00 MB/s")]
    public void FormatRate(double bytesPerSecond, string expected)
        => Assert.Equal(expected, SizeFormatter.FormatRate(bytesPerSecond));
}
