using Photon.Core.Util;

namespace Photon.Tests;

public class PathSanitizerTests
{
    [Theory]
    [InlineData("NIKON/D750", "NIKOND750")]
    [InlineData("a<b>c:d\"e|f?g*h\\i/j", "abcdefghij")]
    [InlineData("photo.jpg", "photo.jpg")]
    [InlineData("Sony A7 III", "Sony A7 III")]
    public void SanitizeSegment_StripsInvalidChars(string input, string expected)
        => Assert.Equal(expected, PathSanitizer.SanitizeSegment(input));

    [Fact]
    public void SanitizeSegment_StripsControlChars()
        => Assert.Equal("ab", PathSanitizer.SanitizeSegment("ab"));

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con", "_con")]
    [InlineData("com1.jpg", "_com1.jpg")]
    [InlineData("LPT9.tmp.jpg", "_LPT9.tmp.jpg")]
    [InlineData("NUL.txt", "_NUL.txt")]
    public void SanitizeSegment_PrefixesReservedNames(string input, string expected)
        => Assert.Equal(expected, PathSanitizer.SanitizeSegment(input));

    [Fact]
    public void SanitizeSegment_ReservedStemOnlyWhenExact()
    {
        // "CONSOLE" is not reserved; only the exact stem before the first dot is.
        Assert.Equal("CONSOLE.jpg", PathSanitizer.SanitizeSegment("CONSOLE.jpg"));
    }

    [Theory]
    [InlineData("name...", "name")]
    [InlineData("name   ", "name")]
    [InlineData("name. . .", "name")]
    [InlineData("  name", "name")]
    public void SanitizeSegment_TrimsTrailingDotsAndSpaces(string input, string expected)
        => Assert.Equal(expected, PathSanitizer.SanitizeSegment(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    [InlineData("...")]
    public void SanitizeSegment_EmptyOrFullyStripped_ReturnsFallback(string input)
        => Assert.Equal("_", PathSanitizer.SanitizeSegment(input));

    [Fact]
    public void SanitizeSegment_CustomFallback()
        => Assert.Equal("Unknown Camera", PathSanitizer.SanitizeSegment("", "Unknown Camera"));

    [Fact]
    public void MakeUnique_NoCollision_ReturnsOriginal()
    {
        var exists = new HashSet<string>();
        Assert.Equal("/x/a.jpg", PathSanitizer.MakeUnique("/x/a.jpg", exists.Contains));
    }

    [Fact]
    public void MakeUnique_AppendsCounterBeforeExtension()
    {
        var exists = new HashSet<string> { "/x/a.jpg" };
        Assert.Equal(Path.Combine("/x", "a_1.jpg"), PathSanitizer.MakeUnique("/x/a.jpg", exists.Contains));

        exists.Add(Path.Combine("/x", "a_1.jpg"));
        exists.Add(Path.Combine("/x", "a_2.jpg"));
        Assert.Equal(Path.Combine("/x", "a_3.jpg"), PathSanitizer.MakeUnique("/x/a.jpg", exists.Contains));
    }

    [Fact]
    public void MakeUnique_NoExtension()
    {
        var exists = new HashSet<string> { "/x/folder" };
        Assert.Equal(Path.Combine("/x", "folder_1"), PathSanitizer.MakeUnique("/x/folder", exists.Contains));
    }
}
