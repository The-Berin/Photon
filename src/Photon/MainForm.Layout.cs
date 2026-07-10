using Photon.Core.Models;

namespace Photon.App;

// Control construction. Fixed "design-time" bounds are set before parenting so the
// anchor offsets are captured correctly; anchors then handle live resizing.
public partial class MainForm
{
    private readonly ToolTip _toolTip = new();

    // Menu
    private MenuStrip _menu = null!;
    private ToolStripMenuItem _miOpenSource = null!, _miOpenOutput = null!, _miRecent = null!, _miExit = null!;
    private ToolStripMenuItem _miCopyLog = null!, _miResetSettings = null!;
    private ToolStripMenuItem _miDarkMode = null!, _miSizeUnit = null!, _miRefreshPreview = null!, _miSeeAllFiles = null!;
    private ToolStripMenuItem _miStart = null!, _miStop = null!, _miPreviewSort = null!, _miUndo = null!, _miHistory = null!;
    private ToolStripMenuItem _miBatchRename = null!, _miDupFinder = null!, _miFolderScan = null!, _miFlatten = null!, _miDriveInspector = null!;
    private ToolStripMenuItem _miAlwaysOnTop = null!;
    private ToolStripMenuItem _miAbout = null!, _miGitHub = null!;
    private readonly List<ToolStripMenuItem> _miSizeUnitItems = [];

    // Status bar
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblStatus = null!, _lblClock = null!;
    private ToolStripComboBox _cboStatusWhenDone = null!;

    // Folders
    private GroupBox _grpFolders = null!;
    private TextBox _txtSource = null!, _txtOutput = null!;
    private Button _btnBrowseSource = null!, _btnBrowseOutput = null!;
    private CheckBox _chkCustomOutput = null!;

    // Preview
    private GroupBox _grpPreview = null!;
    private FlowLayoutPanel _previewFlow = null!;
    private Label _lblPreviewPlaceholder = null!, _lblMoreFiles = null!;
    private Button _btnSeeAll = null!;

    // Run
    private GroupBox _grpRun = null!;
    private Label _lblInfo = null!;
    private Button _btnSort = null!, _btnStop = null!;
    private CheckBox _chkKeepAwakeRun = null!;
    private TextBox _runLog = null!;
    private ProgressBar _progressBar = null!;

    // Tabs
    private TabControl _tabs = null!;
    private RadioButton _rbYearOnly = null!, _rbYearMonth = null!, _rbYearMonthDay = null!;
    private RadioButton _rbMonthNumber = null!, _rbMonthName = null!;
    private CheckBox _chkIncludeTime = null!, _chkGroupCamera = null!;
    private RadioButton _rbExifThenFile = null!, _rbExifOnly = null!, _rbFileThenExif = null!, _rbFileOnly = null!;
    private TextBox _txtUnknownDate = null!;
    private RadioButton _rbDupRename = null!, _rbDupSkip = null!, _rbDupOverwrite = null!;
    private CheckBox _chkDetectDuplicates = null!, _chkMoveDuplicates = null!;
    private CheckBox _chkPictures = null!, _chkVideos = null!, _chkRecursive = null!;
    private TextBox _txtCustomExt = null!;
    private Button _btnResetExt = null!;
    private RadioButton _rbCopy = null!, _rbMove = null!;
    private CheckBox _chkWriteLog = null!, _chkExportCsv = null!;
    private TextBox _txtWav = null!;
    private Button _btnBrowseWav = null!;
    private CheckBox _chkDarkMode = null!, _chkKeepDisplay = null!;
    private ComboBox _cboSizeUnit = null!, _cboWhenDone = null!;

    private static readonly string[] SizeUnitNames = ["Auto", "B", "KB", "MB", "GB", "TB"];
    private static readonly string[] WhenDoneNames = ["Do Nothing", "Open output folder", "Close app", "Sleep", "Shut down"];

    private void BuildUi()
    {
        SuspendLayout();
        Text = "Photon";
        MinimumSize = new Size(1400, 860);
        ClientSize = new Size(1400, 860);
        StartPosition = FormStartPosition.CenterScreen;

        BuildMenu();
        BuildStatusBar();

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(6, 4, 6, 2),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 620));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        content.Controls.Add(BuildLeftColumn(), 0, 0);
        content.Controls.Add(BuildTabs(), 1, 0);

        Controls.Add(content);
        Controls.Add(_statusStrip);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        ResumeLayout(false);
        PerformLayout();
    }

    // ----- helpers -----

    private static Label MakeLabel(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true };

    private static RadioButton MakeRadio(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true };

    private static CheckBox MakeCheck(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true };

    private static ToolStripMenuItem Menu(string text, EventHandler? onClick = null, Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text);
        if (onClick is not null) item.Click += onClick;
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        return item;
    }

    // ----- menu -----

    private void BuildMenu()
    {
        _menu = new MenuStrip();

        _miOpenSource = Menu("&Open source...", (_, _) => BrowseSource(), Keys.Control | Keys.O);
        _miOpenOutput = Menu("Open output &folder", (_, _) => OpenOutputFolder(), Keys.Control | Keys.Shift | Keys.O);
        _miRecent = new ToolStripMenuItem("&Recent sources");
        _miExit = Menu("E&xit", (_, _) => Close());
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange([_miOpenSource, _miOpenOutput, new ToolStripSeparator(), _miRecent, new ToolStripSeparator(), _miExit]);

        _miCopyLog = Menu("&Copy log", (_, _) => CopyLog(), Keys.Control | Keys.Shift | Keys.C);
        _miResetSettings = Menu("&Reset settings to defaults", (_, _) => ResetSettings());
        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.AddRange([_miCopyLog, new ToolStripSeparator(), _miResetSettings]);

        _miDarkMode = new ToolStripMenuItem("&Dark mode") { CheckOnClick = true };
        _miDarkMode.CheckedChanged += (_, _) => { if (!_suppress) ApplyDarkMode(_miDarkMode.Checked); };
        _miSizeUnit = new ToolStripMenuItem("&Size unit");
        foreach (var unit in Enum.GetValues<SizeUnit>())
        {
            var item = new ToolStripMenuItem(SizeUnitNames[(int)unit]) { Tag = unit };
            item.Click += (s, _) => ApplySizeUnit((SizeUnit)((ToolStripMenuItem)s!).Tag!);
            _miSizeUnitItems.Add(item);
            _miSizeUnit.DropDownItems.Add(item);
        }
        _miRefreshPreview = Menu("&Refresh preview", (_, _) => StartScan(), Keys.F5);
        _miSeeAllFiles = Menu("See &all files", (_, _) => ShowSeeAll(), Keys.Control | Keys.Shift | Keys.A);
        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.AddRange([_miDarkMode, _miSizeUnit, new ToolStripSeparator(), _miRefreshPreview, _miSeeAllFiles]);

        _miStart = Menu("&Start", async (_, _) => await RunSortAsync(), Keys.F9);
        _miStop = Menu("S&top", (_, _) => StopRun(), Keys.Control | Keys.F9);
        _miPreviewSort = Menu("&Preview sort (ghost shortcuts)...", async (_, _) => await RunPreviewSortAsync(), Keys.Control | Keys.P);
        _miUndo = Menu("&Undo last sort", (_, _) => UndoLastSort(), Keys.Control | Keys.Z);
        _miHistory = Menu("&History...", (_, _) => ShowHistory(), Keys.Control | Keys.H);
        var sort = new ToolStripMenuItem("&Sort");
        sort.DropDownItems.AddRange([_miStart, _miStop, new ToolStripSeparator(), _miPreviewSort, new ToolStripSeparator(), _miUndo, _miHistory]);

        _miBatchRename = Menu("&Batch Rename...", (_, _) => ShowBatchRename());
        _miDupFinder = Menu("&Duplicate Finder...", (_, _) => ShowDuplicateFinder());
        _miFolderScan = Menu("Folder &Scan...", (_, _) => ShowFolderScan());
        _miFlatten = Menu("Folder &Flatten...", (_, _) => ShowFlatten());
        _miDriveInspector = Menu("Drive &Inspector...", (_, _) => ShowDriveInspector());
        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.AddRange([_miBatchRename, _miDupFinder, _miFolderScan, _miFlatten, _miDriveInspector]);

        _miAlwaysOnTop = new ToolStripMenuItem("&Always on top") { CheckOnClick = true };
        _miAlwaysOnTop.CheckedChanged += (_, _) =>
        {
            if (_suppress) return;
            Settings.AlwaysOnTop = _miAlwaysOnTop.Checked;
            TopMost = _miAlwaysOnTop.Checked;
            ScheduleSave();
        };
        var window = new ToolStripMenuItem("&Window");
        window.DropDownItems.Add(_miAlwaysOnTop);

        _miAbout = Menu("&About Photon", (_, _) => ShowAbout());
        _miGitHub = Menu("Photon on &GitHub", (_, _) => Services.WhenDoneService.OpenUrl(GitHubUrl));
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.AddRange([_miAbout, _miGitHub]);

        _menu.Items.AddRange([file, edit, view, sort, tools, window, help]);
    }

    private void BuildStatusBar()
    {
        _statusStrip = new StatusStrip { ShowItemToolTips = true };
        _lblStatus = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _lblClock = new ToolStripStatusLabel("");
        var lblWhenDone = new ToolStripStatusLabel("When Done:");
        _cboStatusWhenDone = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 150 };
        _cboStatusWhenDone.Items.AddRange(WhenDoneNames);
        _statusStrip.Items.AddRange(new ToolStripItem[] { _lblStatus, _lblClock, lblWhenDone, _cboStatusWhenDone });
    }

    // ----- left column -----

    private Control BuildLeftColumn()
    {
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));

        left.Controls.Add(BuildFoldersGroup(), 0, 0);
        left.Controls.Add(BuildPreviewGroup(), 0, 1);
        left.Controls.Add(BuildRunGroup(), 0, 2);
        return left;
    }

    private Control BuildFoldersGroup()
    {
        _grpFolders = new GroupBox { Text = "Folders", Dock = DockStyle.Fill, Size = new Size(604, 118) };

        var lblSource = MakeLabel("Source:", 10, 27);
        _txtSource = new TextBox
        {
            Location = new Point(76, 23),
            Size = new Size(418, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _btnBrowseSource = new Button
        {
            Text = "Browse...",
            Location = new Point(504, 22),
            Size = new Size(88, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        _chkCustomOutput = MakeCheck("Use custom output (else Source\\Sorted)", 12, 52);

        var lblOutput = MakeLabel("Output:", 10, 84);
        _txtOutput = new TextBox
        {
            Location = new Point(76, 80),
            Size = new Size(418, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Enabled = false,
        };
        _btnBrowseOutput = new Button
        {
            Text = "Browse...",
            Location = new Point(504, 79),
            Size = new Size(88, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Enabled = false,
        };

        _grpFolders.Controls.AddRange([lblSource, _txtSource, _btnBrowseSource, _chkCustomOutput, lblOutput, _txtOutput, _btnBrowseOutput]);
        return _grpFolders;
    }

    private Control BuildPreviewGroup()
    {
        _grpPreview = new GroupBox { Text = "Preview", Dock = DockStyle.Fill, Size = new Size(604, 370) };

        _previewFlow = new FlowLayoutPanel
        {
            Location = new Point(8, 22),
            Size = new Size(588, 306),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _lblPreviewPlaceholder = new Label
        {
            Text = "Select a source folder.",
            Location = new Point(8, 22),
            Size = new Size(588, 306),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _lblMoreFiles = new Label
        {
            Text = "",
            Location = new Point(10, 340),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = SystemColors.GrayText,
        };
        _btnSeeAll = new Button
        {
            Text = "See All",
            Location = new Point(508, 334),
            Size = new Size(88, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Enabled = false,
        };

        _grpPreview.Controls.AddRange([_lblPreviewPlaceholder, _previewFlow, _lblMoreFiles, _btnSeeAll]);
        _lblPreviewPlaceholder.BringToFront();
        return _grpPreview;
    }

    private Control BuildRunGroup()
    {
        _grpRun = new GroupBox { Text = "Run", Dock = DockStyle.Fill, Size = new Size(604, 294) };

        _lblInfo = new Label
        {
            Text = "0 file(s)",
            Location = new Point(10, 22),
            Size = new Size(584, 18),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoEllipsis = true,
        };
        _btnSort = new Button { Text = "Sort pictures", Location = new Point(10, 46), Size = new Size(130, 30), Enabled = false };
        _btnStop = new Button { Text = "Stop", Location = new Point(148, 46), Size = new Size(90, 30), Enabled = false };
        _chkKeepAwakeRun = MakeCheck("Keep Awake", 252, 53);

        _runLog = new TextBox
        {
            Location = new Point(10, 84),
            Size = new Size(584, 168),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
        };
        _progressBar = new ProgressBar
        {
            Location = new Point(10, 260),
            Size = new Size(584, 20),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Style = ProgressBarStyle.Continuous,
        };

        _grpRun.Controls.AddRange([_lblInfo, _btnSort, _btnStop, _chkKeepAwakeRun, _runLog, _progressBar]);
        return _grpRun;
    }

    // ----- right column: tabs -----

    private Control BuildTabs()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) };
        _tabs.TabPages.Add(BuildStructureTab());
        _tabs.TabPages.Add(BuildDuplicatesTab());
        _tabs.TabPages.Add(BuildLogExportTab());
        return _tabs;
    }

    private TabPage BuildStructureTab()
    {
        var page = new TabPage("Structure & date") { AutoScroll = true, Size = new Size(730, 700) };

        var grpStructure = new GroupBox
        {
            Text = "Folder structure",
            Bounds = new Rectangle(10, 10, 710, 188),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _rbYearOnly = MakeRadio("Year only", 14, 24);
        _rbYearMonth = MakeRadio("Year \\ Month", 14, 48);
        _rbYearMonthDay = MakeRadio("Year \\ Month \\ Day", 14, 72);
        var lblMonth = MakeLabel("Month:", 14, 102);
        // Own panel so the month radios form their own group.
        var pnlMonth = new Panel { Bounds = new Rectangle(75, 96, 240, 26) };
        _rbMonthNumber = MakeRadio("Number", 0, 2);
        _rbMonthName = MakeRadio("Name", 95, 2);
        pnlMonth.Controls.AddRange([_rbMonthNumber, _rbMonthName]);
        _chkIncludeTime = MakeCheck("Include time (HH-MM subfolder, e.g. 14-30)", 14, 128);
        _chkGroupCamera = MakeCheck("Group by camera (Make \\ Model)", 14, 154);
        grpStructure.Controls.AddRange([_rbYearOnly, _rbYearMonth, _rbYearMonthDay, lblMonth, pnlMonth, _chkIncludeTime, _chkGroupCamera]);

        var grpDateSource = new GroupBox
        {
            Text = "Date source",
            Bounds = new Rectangle(10, 206, 710, 162),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _rbExifThenFile = MakeRadio("EXIF then file date", 14, 24);
        _rbExifOnly = MakeRadio("EXIF only", 14, 48);
        _rbFileThenExif = MakeRadio("File date then EXIF", 14, 72);
        _rbFileOnly = MakeRadio("File date only", 14, 96);
        var lblUnknownDate = MakeLabel("Folder for files without a date:", 14, 128);
        _txtUnknownDate = new TextBox
        {
            Location = new Point(200, 124),
            Size = new Size(200, 23),
        };
        grpDateSource.Controls.AddRange([_rbExifThenFile, _rbExifOnly, _rbFileThenExif, _rbFileOnly, lblUnknownDate, _txtUnknownDate]);

        page.Controls.AddRange([grpStructure, grpDateSource]);
        return page;
    }

    private TabPage BuildDuplicatesTab()
    {
        var page = new TabPage("Duplicates & files") { AutoScroll = true, Size = new Size(730, 700) };

        var grpDup = new GroupBox
        {
            Text = "Duplicate handling",
            Bounds = new Rectangle(10, 10, 710, 156),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _rbDupRename = MakeRadio("Rename (_1, _2...)", 14, 24);
        _rbDupSkip = MakeRadio("Skip", 14, 48);
        _rbDupOverwrite = MakeRadio("Overwrite", 14, 72);
        _chkDetectDuplicates = MakeCheck("Detect exact duplicates (hash) and move to Duplicates folder", 14, 100);
        _chkMoveDuplicates = MakeCheck("Move duplicate files to Duplicates subfolder", 14, 126);
        grpDup.Controls.AddRange([_rbDupRename, _rbDupSkip, _rbDupOverwrite, _chkDetectDuplicates, _chkMoveDuplicates]);

        var grpMedia = new GroupBox
        {
            Text = "Media types (include)",
            Bounds = new Rectangle(10, 174, 710, 158),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _chkPictures = MakeCheck("Pictures (jpg, png, heic, etc.)", 14, 24);
        _chkVideos = MakeCheck("Videos (mp4, mov, avi, etc.)", 14, 48);
        var lblCustom = MakeLabel("Custom extensions (overrides above if set):", 14, 76);
        _txtCustomExt = new TextBox
        {
            Location = new Point(14, 96),
            Size = new Size(548, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _btnResetExt = new Button
        {
            Text = "Reset to default",
            Location = new Point(572, 95),
            Size = new Size(120, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _chkRecursive = MakeCheck("Include subfolders (recursive)", 14, 128);
        grpMedia.Controls.AddRange([_chkPictures, _chkVideos, lblCustom, _txtCustomExt, _btnResetExt, _chkRecursive]);

        var grpAction = new GroupBox
        {
            Text = "Action",
            Bounds = new Rectangle(10, 340, 710, 82),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _rbCopy = MakeRadio("Copy (keep originals)", 14, 24);
        _rbMove = MakeRadio("Move", 14, 48);
        grpAction.Controls.AddRange([_rbCopy, _rbMove]);

        page.Controls.AddRange([grpDup, grpMedia, grpAction]);
        return page;
    }

    private TabPage BuildLogExportTab()
    {
        var page = new TabPage("Log & export") { AutoScroll = true, Size = new Size(730, 700) };

        _chkWriteLog = MakeCheck("Write log file in output folder (timestamps, actions, errors)", 14, 12);
        _chkExportCsv = MakeCheck("Export CSV summary (original_path, new_path, date, camera, gps)", 14, 38);

        var grpSound = new GroupBox
        {
            Text = "Sound when done",
            Bounds = new Rectangle(10, 68, 710, 84),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        var lblWav = MakeLabel("WAV file (optional, else system beep):", 14, 22);
        _txtWav = new TextBox
        {
            Location = new Point(14, 44),
            Size = new Size(558, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _btnBrowseWav = new Button
        {
            Text = "Browse...",
            Location = new Point(582, 43),
            Size = new Size(110, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        grpSound.Controls.AddRange([lblWav, _txtWav, _btnBrowseWav]);

        var grpAppearance = new GroupBox
        {
            Text = "Appearance",
            Bounds = new Rectangle(10, 160, 710, 56),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _chkDarkMode = MakeCheck("Dark mode", 14, 22);
        grpAppearance.Controls.Add(_chkDarkMode);

        var grpRuntime = new GroupBox
        {
            Text = "Runtime & finish",
            Bounds = new Rectangle(10, 224, 710, 122),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _chkKeepDisplay = MakeCheck("Keep display awake while sorting (prevents monitor off/sleep on Windows)", 14, 22);
        var lblSizeUnit = MakeLabel("Size unit:", 14, 56);
        _cboSizeUnit = new ComboBox
        {
            Location = new Point(104, 52),
            Size = new Size(110, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cboSizeUnit.Items.AddRange(SizeUnitNames);
        var lblWhenDone = MakeLabel("When Done:", 14, 88);
        _cboWhenDone = new ComboBox
        {
            Location = new Point(104, 84),
            Size = new Size(180, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cboWhenDone.Items.AddRange(WhenDoneNames);
        grpRuntime.Controls.AddRange([_chkKeepDisplay, lblSizeUnit, _cboSizeUnit, lblWhenDone, _cboWhenDone]);

        page.Controls.AddRange([_chkWriteLog, _chkExportCsv, grpSound, grpAppearance, grpRuntime]);
        return page;
    }
}
