namespace Photon.App.Forms;

/// <summary>
/// The full rename pattern token list from the RenameOptions contract, grouped by category
/// with human descriptions for the "Insert token" menus. Both the simple and advanced
/// pattern boxes build their menu from here.
/// </summary>
internal static class TokenCatalog
{
    public readonly record struct Token(string Text, string Description);

    public readonly record struct TokenGroup(string Name, Token[] Tokens);

    public static readonly TokenGroup[] Groups =
    [
        new("Name / file",
        [
            new("{name}", "Original file name (without extension)"),
            new("{ext}", "Original extension (jpg)"),
            new("{origext}", "Extension the file had on disk, before extension operations"),
            new("{parent}", "Parent folder name"),
            new("{parent2}", "Grandparent folder name"),
            new("{parent3}", "Great-grandparent folder name"),
            new("{drive}", "Drive letter (C)"),
            new("{depth}", "Folder depth below the loaded folder"),
            new("{size}", "File size, human-readable"),
            new("{sizeMB}", "File size in whole MB"),
            new("{filesize-bytes}", "File size in bytes"),
        ]),
        new("Counters",
        [
            new("{counter}", "Main counter — start / step / padding from Pattern & numbering"),
            new("{counter2}", "Second counter — its own independent start / step / padding"),
        ]),
        new("Date / time",
        [
            new("{yyyy}", "Year, 4 digits (2024)"),
            new("{yy}", "Year, 2 digits (24)"),
            new("{MM}", "Month, 2 digits (06)"),
            new("{MMM}", "Month, short name (Jun)"),
            new("{MMMM}", "Month, full name (June)"),
            new("{dd}", "Day of month, 2 digits (01)"),
            new("{ddd}", "Weekday, short name (Mon)"),
            new("{HH}", "Hour, 24h, 2 digits (14)"),
            new("{hh12}", "Hour, 12h, 2 digits (02)"),
            new("{ampm}", "AM or PM"),
            new("{mm}", "Minute, 2 digits (30)"),
            new("{ss}", "Second, 2 digits (07)"),
            new("{date}", "Full date (2024-06-01)"),
            new("{time}", "Full time (14-30-07)"),
            new("{week}", "ISO week number"),
            new("{quarter}", "Quarter of the year (1-4)"),
            new("{dayofyear}", "Day of the year (153)"),
            new("{weekday}", "Weekday, full name (Monday)"),
            new("{epoch}", "Unix timestamp, seconds"),
            new("{age-days}", "Days between the file's date and today"),
        ]),
        new("Camera / EXIF",
        [
            new("{camera}", "Camera make and model"),
            new("{make}", "Camera make"),
            new("{model}", "Camera model"),
            new("{lens}", "Lens model"),
            new("{artist}", "EXIF artist / author"),
            new("{software}", "Software that created the file"),
            new("{width}", "Pixel width"),
            new("{height}", "Pixel height"),
            new("{mp}", "Megapixels"),
            new("{orientation}", "EXIF orientation"),
            new("{fnumber}", "Aperture (f/2.8)"),
            new("{iso-speed}", "ISO speed"),
            new("{exposure}", "Exposure time"),
            new("{focal}", "Focal length in mm"),
        ]),
        new("Video",
        [
            new("{duration}", "Video length, minutes.seconds (m.ss)"),
            new("{duration-s}", "Video length in whole seconds"),
        ]),
        new("GPS",
        [
            new("{lat}", "Latitude"),
            new("{lon}", "Longitude"),
            new("{gps}", "Latitude and longitude pair"),
        ]),
        new("Identity",
        [
            new("{hash8}", "First 8 hex chars of the content hash"),
            new("{md5-8}", "First 8 hex chars of the MD5 hash"),
            new("{sha1-8}", "First 8 hex chars of the SHA-1 hash"),
            new("{crc32}", "CRC-32 checksum"),
            new("{guid}", "Random GUID"),
            new("{rand4}", "4 random characters"),
            new("{rand8}", "8 random characters"),
        ]),
    ];

    /// <summary>Builds a category-grouped menu that inserts the chosen token into <paramref name="target"/> at the caret.</summary>
    public static ContextMenuStrip BuildInsertMenu(TextBox target)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var group in Groups)
        {
            var category = new ToolStripMenuItem(group.Name);
            foreach (var token in group.Tokens)
            {
                var item = new ToolStripMenuItem($"{token.Text}   —   {token.Description}") { Tag = token.Text };
                item.Click += (_, _) => InsertAtCaret(target, (string)item.Tag!);
                category.DropDownItems.Add(item);
            }
            menu.Items.Add(category);
        }
        return menu;
    }

    private static void InsertAtCaret(TextBox target, string text)
    {
        var caret = target.SelectionStart;
        target.Text = target.Text.Remove(caret, target.SelectionLength).Insert(caret, text);
        target.SelectionStart = caret + text.Length;
        target.Focus();
    }
}
