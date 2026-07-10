namespace Photon.App.Forms;

/// <summary>
/// Advanced mode, continued: the "Remove", "Insert &amp; hygiene", "Case &amp; affixes",
/// "Extension &amp; scope", and "Safety" tabs, plus the control interlocks they share.
/// </summary>
public partial class BatchRenameForm
{
    // ----- Tab 3: Remove -----
    private CheckBox _chkRemoveRange = null!;
    private NumericUpDown _numRemoveStart = null!;
    private NumericUpDown _numRemoveCount = null!;
    private CheckBox _chkRemoveFromEnd = null!;
    private CheckBox _chkRemoveRange2 = null!;
    private NumericUpDown _numRemove2Start = null!;
    private NumericUpDown _numRemove2Count = null!;
    private CheckBox _chkRemove2FromEnd = null!;
    private CheckBox _chkRemoveBetween = null!;
    private TextBox _txtBetweenFrom = null!;
    private TextBox _txtBetweenTo = null!;
    private CheckBox _chkBetweenDelims = null!;
    private CheckBox _chkRemoveCameraPrefixes = null!;
    private CheckBox _chkRemoveDatePatterns = null!;
    private CheckBox _chkRemoveGuids = null!;
    private CheckBox _chkRemoveUrls = null!;
    private CheckBox _chkRemoveBrackets = null!;
    private CheckBox _chkRemovePunctuation = null!;
    private CheckBox _chkRemoveNonAscii = null!;
    private CheckBox _chkRemoveEmoji = null!;
    private CheckBox _chkRemoveNumbers = null!;
    private CheckBox _chkRemoveLeadingNumbers = null!;
    private CheckBox _chkRemoveTrailingNumbers = null!;
    private TextBox _txtRemoveWords = null!;

    // ----- Tab 4: Insert & hygiene -----
    private CheckBox _chkInsert = null!;
    private TextBox _txtInsertText = null!;
    private ComboBox _cmbInsertAnchor = null!;
    private NumericUpDown _numInsertPos = null!;
    private CheckBox _chkInsertFromEnd = null!;
    private TextBox _txtInsertAnchorText = null!;
    private CheckBox _chkInsert2 = null!;
    private TextBox _txtInsert2Text = null!;
    private ComboBox _cmbInsert2Anchor = null!;
    private NumericUpDown _numInsert2Pos = null!;
    private CheckBox _chkInsert2FromEnd = null!;
    private TextBox _txtInsert2AnchorText = null!;
    private CheckBox _chkTrim = null!;
    private CheckBox _chkCollapse = null!;
    private CheckBox _chkUnderscoresToSpaces = null!;
    private CheckBox _chkDotsToSpaces = null!;
    private CheckBox _chkCollapseSeparators = null!;
    private CheckBox _chkTrimSeparators = null!;
    private CheckBox _chkReplaceSpaces = null!;
    private TextBox _txtReplaceSpacesWith = null!;
    private CheckBox _chkDiacritics = null!;
    private CheckBox _chkTransliterate = null!;
    private TextBox _txtStrip = null!;
    private NumericUpDown _numPadRuns = null!;
    private NumericUpDown _numMaxLength = null!;
    private ComboBox _cmbTruncateFrom = null!;

    // ----- Tab 5: Case & affixes -----
    private ComboBox _cmbAdvNameCase = null!;
    private ComboBox _cmbAdvExtCase = null!;
    private CheckBox _chkSmartTitle = null!;
    private TextBox _txtSmallWords = null!;
    private TextBox _txtPreserveWords = null!;
    private TextBox _txtAdvPrefix = null!;
    private CheckBox _chkPrefixIfMissing = null!;
    private TextBox _txtAdvSuffix = null!;
    private CheckBox _chkSuffixIfMissing = null!;
    private CheckBox _chkParentPrefix = null!;
    private TextBox _txtParentSep = null!;

    // ----- Tab 6: Extension & scope -----
    private TextBox _txtNewExtension = null!;
    private CheckBox _chkRemoveExtension = null!;
    private CheckBox _chkNormalizeExt = null!;
    private CheckBox _chkSniffExt = null!;
    private TextBox _txtAdvIncludeMask = null!;
    private TextBox _txtAdvExcludeMask = null!;
    private CheckBox _chkRegexMasks = null!;
    private CheckBox _chkAdvSubfolders = null!;
    private NumericUpDown _numMinSize = null!;
    private ComboBox _cmbMinSizeUnit = null!;
    private NumericUpDown _numMaxSize = null!;
    private ComboBox _cmbMaxSizeUnit = null!;
    private DateTimePicker _dtpModifiedAfter = null!;
    private DateTimePicker _dtpModifiedBefore = null!;
    private CheckBox _chkOnlyWithExif = null!;
    private CheckBox _chkSkipHidden = null!;

    // ----- Tab 7: Safety -----
    private RadioButton _radConflictAppend = null!;
    private RadioButton _radConflictSkip = null!;
    private RadioButton _radConflictFail = null!;
    private TextBox _txtCollisionFormat = null!;
    private Label _lblCollisionExample = null!;
    private CheckBox _chkExportCsv = null!;

    // ---------- Tab 3: Remove ----------

    private void BuildRemoveTab(TabControl tabs)
    {
        var stack = NewTabStack(tabs, "Remove");

        var gbRanges = NewAdvGroup("Remove character ranges", 86);
        _chkRemoveRange = new CheckBox { Text = "Range 1 —", Location = new Point(10, 24), AutoSize = true };
        gbRanges.Controls.Add(NewInlineLabel("at position:", 104, 27));
        _numRemoveStart = new NumericUpDown { Location = new Point(176, 24), Width = 54, Minimum = 0, Maximum = 9999 };
        gbRanges.Controls.Add(NewInlineLabel("count:", 240, 27));
        _numRemoveCount = new NumericUpDown { Location = new Point(284, 24), Width = 54, Minimum = 0, Maximum = 9999 };
        _chkRemoveFromEnd = new CheckBox { Text = "from end", Location = new Point(348, 25), AutoSize = true };
        _chkRemoveRange2 = new CheckBox { Text = "Range 2 —", Location = new Point(10, 52), AutoSize = true };
        gbRanges.Controls.Add(NewInlineLabel("at position:", 104, 55));
        _numRemove2Start = new NumericUpDown { Location = new Point(176, 52), Width = 54, Minimum = 0, Maximum = 9999 };
        gbRanges.Controls.Add(NewInlineLabel("count:", 240, 55));
        _numRemove2Count = new NumericUpDown { Location = new Point(284, 52), Width = 54, Minimum = 0, Maximum = 9999 };
        _chkRemove2FromEnd = new CheckBox { Text = "from end", Location = new Point(348, 53), AutoSize = true };
        gbRanges.Controls.AddRange([_chkRemoveRange, _numRemoveStart, _numRemoveCount, _chkRemoveFromEnd,
            _chkRemoveRange2, _numRemove2Start, _numRemove2Count, _chkRemove2FromEnd]);

        var gbBetween = NewAdvGroup("Remove between delimiters", 58);
        _chkRemoveBetween = new CheckBox { Text = "Between", Location = new Point(10, 25), AutoSize = true };
        gbBetween.Controls.Add(NewInlineLabel("from:", 90, 27));
        _txtBetweenFrom = new TextBox { Location = new Point(126, 23), Width = 56 };
        gbBetween.Controls.Add(NewInlineLabel("to:", 194, 27));
        _txtBetweenTo = new TextBox { Location = new Point(218, 23), Width = 56 };
        _chkBetweenDelims = new CheckBox { Text = "remove delimiters too", Location = new Point(286, 24), AutoSize = true, Checked = true };
        _tips.SetToolTip(_chkRemoveBetween, "Removes everything from the first \"from\" text to the next \"to\" text.");
        gbBetween.Controls.AddRange([_chkRemoveBetween, _txtBetweenFrom, _txtBetweenTo, _chkBetweenDelims]);

        var gbPatterns = NewAdvGroup("Remove patterns", 184);
        _chkRemoveCameraPrefixes = new CheckBox { Text = "Camera prefixes (IMG_, DSC_…)", Location = new Point(10, 22), AutoSize = true };
        _tips.SetToolTip(_chkRemoveCameraPrefixes, "Strips IMG_ IMG- DSC_ DSCN DSCF DCIM PXL_ VID_ MVI_ GOPR \"Screenshot_\" and \"Screen Shot \".");
        _chkRemoveDatePatterns = new CheckBox { Text = "Embedded dates (2024-06-01…)", Location = new Point(10, 48), AutoSize = true };
        _tips.SetToolTip(_chkRemoveDatePatterns, "Strips date-like runs: 20240601, 2024-06-01, 01.06.2024 and similar.");
        _chkRemoveGuids = new CheckBox { Text = "GUIDs (8-4-4-4-12 hex)", Location = new Point(10, 74), AutoSize = true };
        _chkRemoveUrls = new CheckBox { Text = "URLs (http://…, www.…)", Location = new Point(10, 100), AutoSize = true };
        _chkRemoveBrackets = new CheckBox { Text = "(Bracketed) and [bracketed] text", Location = new Point(10, 126), AutoSize = true };
        _chkRemovePunctuation = new CheckBox { Text = "Punctuation", Location = new Point(10, 152), AutoSize = true };
        _tips.SetToolTip(_chkRemovePunctuation, "Removes punctuation marks like ! ? , ; : ' \" — separators and digits stay.");
        _chkRemoveNonAscii = new CheckBox { Text = "Non-ASCII characters", Location = new Point(240, 22), AutoSize = true };
        _chkRemoveEmoji = new CheckBox { Text = "Emoji", Location = new Point(240, 48), AutoSize = true };
        _chkRemoveNumbers = new CheckBox { Text = "All digits 0-9", Location = new Point(240, 74), AutoSize = true };
        _chkRemoveLeadingNumbers = new CheckBox { Text = "Leading numbers", Location = new Point(240, 100), AutoSize = true };
        _tips.SetToolTip(_chkRemoveLeadingNumbers, "Digits at the very start of the name.");
        _chkRemoveTrailingNumbers = new CheckBox { Text = "Trailing numbers", Location = new Point(240, 126), AutoSize = true };
        _tips.SetToolTip(_chkRemoveTrailingNumbers, "Digits at the very end of the name.");
        gbPatterns.Controls.AddRange([_chkRemoveCameraPrefixes, _chkRemoveDatePatterns, _chkRemoveGuids,
            _chkRemoveUrls, _chkRemoveBrackets, _chkRemovePunctuation, _chkRemoveNonAscii, _chkRemoveEmoji,
            _chkRemoveNumbers, _chkRemoveLeadingNumbers, _chkRemoveTrailingNumbers]);

        var gbWords = NewAdvGroup("Remove words", 58);
        gbWords.Controls.Add(NewInlineLabel("Words:", 10, 27));
        _txtRemoveWords = new TextBox { Location = new Point(58, 24), Width = 398 };
        _tips.SetToolTip(_txtRemoveWords, "Comma or space separated, whole-word, case-insensitive — e.g. \"copy final edited\".");
        gbWords.Controls.Add(_txtRemoveWords);

        stack.Controls.AddRange([gbRanges, gbBetween, gbPatterns, gbWords]);

        WireChange(_chkRemoveRange, _numRemoveStart, _numRemoveCount, _chkRemoveFromEnd,
            _chkRemoveRange2, _numRemove2Start, _numRemove2Count, _chkRemove2FromEnd,
            _chkRemoveBetween, _txtBetweenFrom, _txtBetweenTo, _chkBetweenDelims,
            _chkRemoveCameraPrefixes, _chkRemoveDatePatterns, _chkRemoveGuids, _chkRemoveUrls,
            _chkRemoveBrackets, _chkRemovePunctuation, _chkRemoveNonAscii, _chkRemoveEmoji,
            _chkRemoveNumbers, _chkRemoveLeadingNumbers, _chkRemoveTrailingNumbers, _txtRemoveWords);
    }

    // ---------- Tab 4: Insert & hygiene ----------

    private void BuildInsertHygieneTab(TabControl tabs)
    {
        var stack = NewTabStack(tabs, "Insert & hygiene");

        var gbInsert1 = NewInsertGroup("Insert 1", out _chkInsert, out _txtInsertText,
            out _cmbInsertAnchor, out _numInsertPos, out _chkInsertFromEnd, out _txtInsertAnchorText);
        var gbInsert2 = NewInsertGroup("Insert 2", out _chkInsert2, out _txtInsert2Text,
            out _cmbInsert2Anchor, out _numInsert2Pos, out _chkInsert2FromEnd, out _txtInsert2AnchorText);

        var gbSpaces = NewAdvGroup("Whitespace && separators", 136);
        _chkTrim = new CheckBox { Text = "Trim outer whitespace", Location = new Point(10, 22), AutoSize = true, Checked = true };
        _chkCollapse = new CheckBox { Text = "Collapse repeated spaces", Location = new Point(240, 22), AutoSize = true };
        _chkUnderscoresToSpaces = new CheckBox { Text = "Underscores → spaces", Location = new Point(10, 48), AutoSize = true };
        _chkDotsToSpaces = new CheckBox { Text = "Dots → spaces", Location = new Point(240, 48), AutoSize = true };
        _tips.SetToolTip(_chkDotsToSpaces, "Dots inside the name only — the extension dot is never touched.");
        _chkCollapseSeparators = new CheckBox { Text = "Collapse repeated separators", Location = new Point(10, 74), AutoSize = true };
        _tips.SetToolTip(_chkCollapseSeparators, "Runs of - _ . and spaces mixed together become a single occurrence.");
        _chkTrimSeparators = new CheckBox { Text = "Trim edge separators", Location = new Point(240, 74), AutoSize = true };
        _tips.SetToolTip(_chkTrimSeparators, "Removes leading/trailing space, dash, underscore, and dot from the name.");
        _chkReplaceSpaces = new CheckBox { Text = "Replace spaces with:", Location = new Point(10, 102), AutoSize = true };
        _txtReplaceSpacesWith = new TextBox { Location = new Point(148, 99), Width = 60, Text = "_", Enabled = false };
        gbSpaces.Controls.AddRange([_chkTrim, _chkCollapse, _chkUnderscoresToSpaces, _chkDotsToSpaces,
            _chkCollapseSeparators, _chkTrimSeparators, _chkReplaceSpaces, _txtReplaceSpacesWith]);

        var gbChars = NewAdvGroup("Characters", 110);
        _chkDiacritics = new CheckBox { Text = "Remove diacritics (é → e)", Location = new Point(10, 22), AutoSize = true };
        _chkTransliterate = new CheckBox { Text = "Transliterate to ASCII", Location = new Point(240, 22), AutoSize = true };
        _tips.SetToolTip(_chkTransliterate, "Best-effort full conversion to plain ASCII — a superset of removing diacritics.");
        gbChars.Controls.Add(NewInlineLabel("Strip characters:", 10, 53));
        _txtStrip = new TextBox { Location = new Point(112, 50), Width = 120 };
        _tips.SetToolTip(_txtStrip, "Every character typed here is deleted from names, e.g. #&!");
        gbChars.Controls.Add(NewInlineLabel("Pad number runs to:", 10, 81));
        _numPadRuns = new NumericUpDown { Location = new Point(136, 78), Width = 54, Minimum = 0, Maximum = 12 };
        gbChars.Controls.Add(NewInlineLabel("digits (0 = off)", 196, 81));
        _tips.SetToolTip(_numPadRuns, "Zero-pads every digit run in the name — \"img2\" becomes \"img002\" at width 3.");
        gbChars.Controls.AddRange([_chkDiacritics, _chkTransliterate, _txtStrip, _numPadRuns]);

        var gbLength = NewAdvGroup("Length limit", 58);
        gbLength.Controls.Add(NewInlineLabel("Max name length:", 10, 27));
        _numMaxLength = new NumericUpDown { Location = new Point(118, 24), Width = 64, Minimum = 0, Maximum = 255 };
        _tips.SetToolTip(_numMaxLength, "Hard cap on the name before the extension — 0 means no limit.");
        gbLength.Controls.Add(NewInlineLabel("Cut from:", 208, 27));
        _cmbTruncateFrom = new ComboBox { Location = new Point(272, 24), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        // Order mirrors the TruncateFrom enum.
        _cmbTruncateFrom.Items.AddRange(["the end", "the start", "the middle"]);
        _cmbTruncateFrom.SelectedIndex = 0;
        gbLength.Controls.AddRange([_numMaxLength, _cmbTruncateFrom]);

        stack.Controls.AddRange([gbInsert1, gbInsert2, gbSpaces, gbChars, gbLength]);

        WireChange(_chkInsert, _txtInsertText, _cmbInsertAnchor, _numInsertPos, _chkInsertFromEnd, _txtInsertAnchorText,
            _chkInsert2, _txtInsert2Text, _cmbInsert2Anchor, _numInsert2Pos, _chkInsert2FromEnd, _txtInsert2AnchorText,
            _chkTrim, _chkCollapse, _chkUnderscoresToSpaces, _chkDotsToSpaces, _chkCollapseSeparators,
            _chkTrimSeparators, _chkReplaceSpaces, _txtReplaceSpacesWith,
            _chkDiacritics, _chkTransliterate, _txtStrip, _numPadRuns, _numMaxLength, _cmbTruncateFrom);
    }

    private GroupBox NewInsertGroup(string title, out CheckBox chk, out TextBox txt, out ComboBox anchor,
        out NumericUpDown pos, out CheckBox fromEnd, out TextBox anchorText)
    {
        var gb = NewAdvGroup(title, 88);
        chk = new CheckBox { Text = "Insert", Location = new Point(10, 24), AutoSize = true };
        txt = new TextBox { Location = new Point(72, 22), Width = 176 };
        anchor = new ComboBox { Location = new Point(258, 22), Width = 108, DropDownStyle = ComboBoxStyle.DropDownList };
        // Order mirrors the InsertAnchor enum.
        anchor.Items.AddRange(["at position", "before text", "after text"]);
        anchor.SelectedIndex = 0;
        _tips.SetToolTip(anchor, "Where the text goes: a character index, or before/after the first occurrence of the anchor text.");
        gb.Controls.Add(NewInlineLabel("Position:", 10, 55));
        pos = new NumericUpDown { Location = new Point(68, 52), Width = 54, Minimum = 0, Maximum = 9999 };
        fromEnd = new CheckBox { Text = "from end", Location = new Point(132, 54), AutoSize = true };
        gb.Controls.Add(NewInlineLabel("Anchor:", 226, 55));
        anchorText = new TextBox { Location = new Point(278, 52), Width = 168 };
        gb.Controls.AddRange([chk, txt, anchor, pos, fromEnd, anchorText]);
        return gb;
    }

    // ---------- Tab 5: Case & affixes ----------

    private void BuildCaseAffixTab(TabControl tabs)
    {
        var stack = NewTabStack(tabs, "Case & affixes");

        var gbCase = NewAdvGroup("Case transforms", 140);
        gbCase.Controls.Add(NewInlineLabel("Name:", 10, 27));
        _cmbAdvNameCase = NewCaseCombo(56, 24, 160);
        gbCase.Controls.Add(NewInlineLabel("Extension:", 240, 27));
        _cmbAdvExtCase = NewCaseCombo(306, 24, 140);
        _chkSmartTitle = new CheckBox { Text = "Smart Title Case — keep small words lowercase", Location = new Point(10, 54), AutoSize = true, Checked = true };
        _tips.SetToolTip(_chkSmartTitle, "Title Case keeps the words below lowercase unless they are first or last.");
        gbCase.Controls.Add(NewInlineLabel("Small words:", 10, 83));
        _txtSmallWords = new TextBox
        {
            Location = new Point(92, 80), Width = 364,
            Text = "a an and as at but by for in nor of on or so the to up yet",
        };
        gbCase.Controls.Add(NewInlineLabel("Preserve casing:", 10, 111));
        _txtPreserveWords = new TextBox { Location = new Point(112, 108), Width = 344 };
        _tips.SetToolTip(_txtPreserveWords, "Words whose typed casing survives every case transform, e.g. \"USA iPhone HDR 4K\".");
        gbCase.Controls.AddRange([_cmbAdvNameCase, _cmbAdvExtCase, _chkSmartTitle, _txtSmallWords, _txtPreserveWords]);

        var gbAffix = NewAdvGroup("Prefix / suffix", 86);
        gbAffix.Controls.Add(NewInlineLabel("Prefix:", 10, 27));
        _txtAdvPrefix = new TextBox { Location = new Point(56, 24), Width = 180 };
        _chkPrefixIfMissing = new CheckBox { Text = "only if missing", Location = new Point(250, 26), AutoSize = true, Checked = true };
        _tips.SetToolTip(_chkPrefixIfMissing, "Skip the prefix when the name already starts with it.");
        gbAffix.Controls.Add(NewInlineLabel("Suffix:", 10, 55));
        _txtAdvSuffix = new TextBox { Location = new Point(56, 52), Width = 180 };
        _chkSuffixIfMissing = new CheckBox { Text = "only if missing", Location = new Point(250, 54), AutoSize = true, Checked = true };
        _tips.SetToolTip(_chkSuffixIfMissing, "Skip the suffix when the name already ends with it.");
        gbAffix.Controls.AddRange([_txtAdvPrefix, _chkPrefixIfMissing, _txtAdvSuffix, _chkSuffixIfMissing]);

        var gbParent = NewAdvGroup("Parent folder", 58);
        _chkParentPrefix = new CheckBox { Text = "Prepend parent folder", Location = new Point(10, 26), AutoSize = true };
        _tips.SetToolTip(_chkParentPrefix, "\"Holiday\\beach.jpg\" becomes \"Holiday - beach.jpg\".");
        gbParent.Controls.Add(NewInlineLabel("separated by:", 196, 27));
        _txtParentSep = new TextBox { Location = new Point(278, 24), Width = 80, Text = " - " };
        gbParent.Controls.AddRange([_chkParentPrefix, _txtParentSep]);

        stack.Controls.AddRange([gbCase, gbAffix, gbParent]);

        WireChange(_cmbAdvNameCase, _cmbAdvExtCase, _chkSmartTitle, _txtSmallWords, _txtPreserveWords,
            _txtAdvPrefix, _chkPrefixIfMissing, _txtAdvSuffix, _chkSuffixIfMissing,
            _chkParentPrefix, _txtParentSep);
    }

    // ---------- Tab 6: Extension & scope ----------

    private void BuildExtensionScopeTab(TabControl tabs)
    {
        var stack = NewTabStack(tabs, "Extension & scope");

        var gbExt = NewAdvGroup("Extension", 110);
        gbExt.Controls.Add(NewInlineLabel("New extension:", 10, 27));
        _txtNewExtension = new TextBox { Location = new Point(104, 24), Width = 80 };
        _tips.SetToolTip(_txtNewExtension, "Replaces the extension outright, without the dot (e.g. \"jpg\") — empty keeps it.");
        _chkRemoveExtension = new CheckBox { Text = "Remove extension", Location = new Point(240, 26), AutoSize = true };
        _chkNormalizeExt = new CheckBox { Text = "Normalize synonyms (jpeg → jpg, tiff → tif)", Location = new Point(10, 54), AutoSize = true };
        _chkSniffExt = new CheckBox { Text = "Fix lying extensions by sniffing content", Location = new Point(10, 80), AutoSize = true };
        _tips.SetToolTip(_chkSniffExt, "Reads magic bytes (JPEG, PNG, GIF, BMP, TIFF, HEIC, MP4, MOV, AVI, MKV/WebM) and corrects a wrong extension.");
        gbExt.Controls.AddRange([_txtNewExtension, _chkRemoveExtension, _chkNormalizeExt, _chkSniffExt]);

        var gbScope = NewAdvGroup("Scope — which loaded files get renamed", 226);
        gbScope.Controls.Add(NewInlineLabel("Include mask:", 10, 27));
        _txtAdvIncludeMask = new TextBox { Location = new Point(98, 24), Width = 350 };
        gbScope.Controls.Add(NewInlineLabel("Exclude mask:", 10, 55));
        _txtAdvExcludeMask = new TextBox { Location = new Point(98, 52), Width = 350 };
        _chkRegexMasks = new CheckBox { Text = "Masks are regular expressions", Location = new Point(10, 80), AutoSize = true };
        _chkAdvSubfolders = new CheckBox { Text = "Include subfolders (applies on next Load)", Location = new Point(10, 106), AutoSize = true };
        gbScope.Controls.Add(NewInlineLabel("Min size:", 10, 137));
        _numMinSize = new NumericUpDown { Location = new Point(68, 134), Width = 76, Minimum = 0, Maximum = 999_999_999, ThousandsSeparator = true };
        _cmbMinSizeUnit = NewSizeUnitCombo(150, 134);
        gbScope.Controls.Add(NewInlineLabel("Max size:", 240, 137));
        _numMaxSize = new NumericUpDown { Location = new Point(300, 134), Width = 76, Minimum = 0, Maximum = 999_999_999, ThousandsSeparator = true };
        _cmbMaxSizeUnit = NewSizeUnitCombo(382, 134);
        _tips.SetToolTip(_numMinSize, "Files smaller than this are left alone — 0 means no lower bound.");
        _tips.SetToolTip(_numMaxSize, "Files larger than this are left alone — 0 means no upper bound.");
        gbScope.Controls.Add(NewInlineLabel("Modified after:", 10, 167));
        _dtpModifiedAfter = NewNullableDatePicker(104, 163);
        gbScope.Controls.Add(NewInlineLabel("before:", 256, 167));
        _dtpModifiedBefore = NewNullableDatePicker(304, 163);
        _tips.SetToolTip(_dtpModifiedAfter, "Tick the box to filter by modified date — unticked means no limit.");
        _tips.SetToolTip(_dtpModifiedBefore, "Tick the box to filter by modified date — unticked means no limit.");
        _chkOnlyWithExif = new CheckBox { Text = "Only files with an EXIF date", Location = new Point(10, 194), AutoSize = true };
        _tips.SetToolTip(_chkOnlyWithExif, "Skips files whose metadata yields no EXIF/container date.");
        _chkSkipHidden = new CheckBox { Text = "Skip hidden && system files", Location = new Point(240, 194), AutoSize = true, Checked = true };
        gbScope.Controls.AddRange([_txtAdvIncludeMask, _txtAdvExcludeMask, _chkRegexMasks, _chkAdvSubfolders,
            _numMinSize, _cmbMinSizeUnit, _numMaxSize, _cmbMaxSizeUnit,
            _dtpModifiedAfter, _dtpModifiedBefore, _chkOnlyWithExif, _chkSkipHidden]);

        stack.Controls.AddRange([gbExt, gbScope]);

        _txtAdvIncludeMask.TextChanged += (_, _) => SyncScopeFromAdvanced();
        _txtAdvExcludeMask.TextChanged += (_, _) => SyncScopeFromAdvanced();
        _chkAdvSubfolders.CheckedChanged += (_, _) => SyncScopeFromAdvanced();

        WireChange(_txtNewExtension, _chkRemoveExtension, _chkNormalizeExt, _chkSniffExt,
            _chkRegexMasks, _numMinSize, _cmbMinSizeUnit, _numMaxSize, _cmbMaxSizeUnit,
            _dtpModifiedAfter, _dtpModifiedBefore, _chkOnlyWithExif, _chkSkipHidden);
    }

    private static ComboBox NewSizeUnitCombo(int x, int y)
    {
        var cmb = new ComboBox { Location = new Point(x, y), Width = 54, DropDownStyle = ComboBoxStyle.DropDownList };
        // Index order matches SizeUnitMultipliers.
        cmb.Items.AddRange(["B", "KB", "MB", "GB"]);
        cmb.SelectedIndex = 2;
        return cmb;
    }

    private static DateTimePicker NewNullableDatePicker(int x, int y) => new()
    {
        Location = new Point(x, y),
        Width = 140,
        ShowCheckBox = true,
        Checked = false,
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "yyyy-MM-dd",
    };

    // ---------- Tab 7: Safety ----------

    private void BuildSafetyTab(TabControl tabs)
    {
        var stack = NewTabStack(tabs, "Safety");

        var gbConflict = NewAdvGroup("When the new name already exists", 156);
        _radConflictAppend = new RadioButton { Text = "Append a number (photo.jpg → photo_1.jpg)", Location = new Point(10, 22), AutoSize = true, Checked = true };
        _radConflictSkip = new RadioButton { Text = "Skip that file", Location = new Point(10, 46), AutoSize = true };
        _radConflictFail = new RadioButton { Text = "Mark it as a problem and don't rename it", Location = new Point(10, 70), AutoSize = true };
        gbConflict.Controls.Add(NewInlineLabel("Collision suffix:", 10, 103));
        _txtCollisionFormat = new TextBox { Location = new Point(110, 100), Width = 100, Text = "_{n}" };
        _tips.SetToolTip(_txtCollisionFormat, "Template for the appended suffix — {n} is the number. \"_{n}\" gives photo_1.jpg, \" ({n})\" gives photo (1).jpg.");
        _lblCollisionExample = new Label
        {
            Location = new Point(10, 128),
            Size = new Size(448, 18),
            ForeColor = SystemColors.GrayText,
        };
        gbConflict.Controls.AddRange([_radConflictAppend, _radConflictSkip, _radConflictFail,
            _txtCollisionFormat, _lblCollisionExample]);

        var gbAudit = NewAdvGroup("Audit", 56);
        _chkExportCsv = new CheckBox
        {
            Text = "Write an old → new mapping CSV next to the first renamed file",
            Location = new Point(10, 24),
            AutoSize = true,
        };
        gbAudit.Controls.Add(_chkExportCsv);

        stack.Controls.AddRange([gbConflict, gbAudit]);

        WireChange(_radConflictAppend, _radConflictSkip, _radConflictFail, _txtCollisionFormat, _chkExportCsv);
    }

    // ---------- shared interlocks & helpers ----------

    /// <summary>Enables/disables dependent controls so impossible combinations can't be entered.</summary>
    private void UpdateAdvancedInterlocks()
    {
        _txtSwapSeparator.Enabled = _chkSwap.Checked;
        _txtReplaceSpacesWith.Enabled = _chkReplaceSpaces.Checked;
        _txtSmallWords.Enabled = _chkSmartTitle.Checked;
        _txtParentSep.Enabled = _chkParentPrefix.Checked;

        bool atPos1 = _cmbInsertAnchor.SelectedIndex == 0;
        _numInsertPos.Enabled = atPos1;
        _chkInsertFromEnd.Enabled = atPos1;
        _txtInsertAnchorText.Enabled = !atPos1;
        bool atPos2 = _cmbInsert2Anchor.SelectedIndex == 0;
        _numInsert2Pos.Enabled = atPos2;
        _chkInsert2FromEnd.Enabled = atPos2;
        _txtInsert2AnchorText.Enabled = !atPos2;

        // With no extension left, the other extension operations are meaningless.
        bool keepsExtension = !_chkRemoveExtension.Checked;
        _txtNewExtension.Enabled = keepsExtension;
        _chkNormalizeExt.Enabled = keepsExtension;
        _chkSniffExt.Enabled = keepsExtension;
    }

    private void UpdateCollisionExample()
    {
        var format = _txtCollisionFormat.Text;
        var suffix = format.Contains("{n}") ? format.Replace("{n}", "1") : format + "1";
        _lblCollisionExample.Text = $"Example: photo.jpg → photo{suffix}.jpg";
    }
}
