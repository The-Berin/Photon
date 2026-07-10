namespace Photon.App.Forms;

/// <summary>The rename pattern tokens RenameOptions documents, with human descriptions for the "Insert token" menus.</summary>
internal static class TokenCatalog
{
    public readonly record struct Token(string Text, string Description);

    public static readonly Token[] All =
    [
        new("{name}", "Original file name (without extension)"),
        new("{ext}", "Original extension"),
        new("{counter}", "Sequential counter (uses start / step / padding)"),
        new("{yyyy}", "Year, 4 digits (2024)"),
        new("{yy}", "Year, 2 digits (24)"),
        new("{MM}", "Month, 2 digits (06)"),
        new("{MMM}", "Month, short name (Jun)"),
        new("{MMMM}", "Month, full name (June)"),
        new("{dd}", "Day of month, 2 digits (01)"),
        new("{ddd}", "Weekday, short name (Mon)"),
        new("{HH}", "Hour, 24h, 2 digits (14)"),
        new("{mm}", "Minute, 2 digits (30)"),
        new("{ss}", "Second, 2 digits (07)"),
        new("{date}", "Full date (2024-06-01)"),
        new("{time}", "Full time (14-30-07)"),
        new("{camera}", "Camera make and model"),
        new("{make}", "Camera make"),
        new("{model}", "Camera model"),
        new("{width}", "Pixel width"),
        new("{height}", "Pixel height"),
        new("{mp}", "Megapixels"),
        new("{size}", "File size in bytes"),
        new("{sizeMB}", "File size in MB"),
        new("{parent}", "Parent folder name"),
        new("{parent2}", "Grandparent folder name"),
        new("{hash8}", "First 8 hex chars of the content hash"),
        new("{guid}", "Random GUID"),
        new("{rand4}", "4 random characters"),
        new("{rand8}", "8 random characters"),
    ];

    /// <summary>Builds a menu that inserts the chosen token into <paramref name="target"/> at the caret.</summary>
    public static ContextMenuStrip BuildInsertMenu(TextBox target)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var token in All)
        {
            var item = new ToolStripMenuItem($"{token.Text}   —   {token.Description}") { Tag = token.Text };
            item.Click += (_, _) =>
            {
                var text = (string)item.Tag!;
                var caret = target.SelectionStart;
                target.Text = target.Text.Remove(caret, target.SelectionLength).Insert(caret, text);
                target.SelectionStart = caret + text.Length;
                target.Focus();
            };
            menu.Items.Add(item);
        }
        return menu;
    }
}
