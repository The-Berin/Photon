using System.Text.RegularExpressions;
using Photon.App.Services;

namespace Photon.App.Forms;

/// <summary>
/// Advanced mode — a seven-tab control center binding every RenameOptions field.
/// This file owns the shell, the "Pattern &amp; numbering" and "Find &amp; replace" tabs,
/// and the rules-grid plumbing; the other five tabs live in BatchRenameForm.AdvancedPanel.Tabs.cs.
/// </summary>
public partial class BatchRenameForm
{
    private const int AdvGroupWidth = 468;

    // ----- Tab 1: Pattern & numbering -----
    private TextBox _txtAdvPattern = null!;
    private ComboBox _cmbDateSource = null!;
    private NumericUpDown _numAdvStart = null!;
    private NumericUpDown _numAdvStep = null!;
    private NumericUpDown _numAdvPad = null!;
    private ComboBox _cmbCounterStyle = null!;
    private CheckBox _chkCounterPerFolder = null!;
    private NumericUpDown _numCounter2Start = null!;
    private NumericUpDown _numCounter2Step = null!;
    private NumericUpDown _numCounter2Pad = null!;
    private ComboBox _cmbNumberingOrder = null!;

    // ----- Tab 2: Find & replace -----
    private DataGridView _rulesGrid = null!;
    private Label _lblRulesStatus = null!;
    private const int RuleColOn = 0, RuleColFind = 1, RuleColReplace = 2, RuleColRegex = 3,
        RuleColCase = 4, RuleColWord = 5, RuleColFirst = 6, RuleColTarget = 7;
    private CheckBox _chkSwap = null!;
    private TextBox _txtSwapSeparator = null!;

    private void BuildAdvancedPanel()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Multiline = true };

        BuildPatternTab(tabs);
        BuildFindReplaceTab(tabs);
        BuildRemoveTab(tabs);
        BuildInsertHygieneTab(tabs);
        BuildCaseAffixTab(tabs);
        BuildExtensionScopeTab(tabs);
        BuildSafetyTab(tabs);

        _advancedHost.Controls.Add(tabs);

        // Enable/disable relationships between controls; re-evaluated on every relevant toggle.
        _chkSwap.CheckedChanged += (_, _) => UpdateAdvancedInterlocks();
        _chkReplaceSpaces.CheckedChanged += (_, _) => UpdateAdvancedInterlocks();
        _chkSmartTitle.CheckedChanged += (_, _) => UpdateAdvancedInterlocks();
        _chkParentPrefix.CheckedChanged += (_, _) => UpdateAdvancedInterlocks();
        _chkRemoveExtension.CheckedChanged += (_, _) => UpdateAdvancedInterlocks();
        _cmbInsertAnchor.SelectedIndexChanged += (_, _) => UpdateAdvancedInterlocks();
        _cmbInsert2Anchor.SelectedIndexChanged += (_, _) => UpdateAdvancedInterlocks();
        _txtCollisionFormat.TextChanged += (_, _) => UpdateCollisionExample();
        UpdateAdvancedInterlocks();
        UpdateCollisionExample();
    }

    /// <summary>Adds a tab page hosting a top-down scrollable stack of group boxes.</summary>
    private static FlowLayoutPanel NewTabStack(TabControl tabs, string title)
    {
        var page = new TabPage(title);
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4),
        };
        page.Controls.Add(stack);
        tabs.TabPages.Add(page);
        return stack;
    }

    private static GroupBox NewAdvGroup(string title, int height) => new()
    {
        Text = title,
        Width = AdvGroupWidth,
        Height = height,
        Margin = new Padding(3, 3, 3, 6),
    };

    // ---------- Tab 1: Pattern & numbering ----------

    private void BuildPatternTab(TabControl tabs)
    {
        var stack = NewTabStack(tabs, "Pattern & numbering");

        var gbPattern = NewAdvGroup("Pattern && date source", 88);
        _txtAdvPattern = new TextBox { Location = new Point(10, 24), Width = 340, Text = "{name}" };
        var btnToken = new Button { Text = "Insert token ▾", Location = new Point(356, 22), Width = 100 };
        var tokenMenu = TokenCatalog.BuildInsertMenu(_txtAdvPattern);
        btnToken.Click += (_, _) => tokenMenu.Show(btnToken, new Point(0, btnToken.Height));
        gbPattern.Controls.Add(NewInlineLabel("Date tokens use:", 10, 57));
        _cmbDateSource = new ComboBox
        {
            Location = new Point(112, 53), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList,
        };
        // Order mirrors the DateSource enum.
        _cmbDateSource.Items.AddRange(["EXIF date, then file date", "EXIF date only", "File date, then EXIF", "File date only"]);
        _cmbDateSource.SelectedIndex = 0;
        gbPattern.Controls.AddRange([_txtAdvPattern, btnToken, _cmbDateSource]);

        var gbCounter = NewAdvGroup("Counter — {counter}", 112);
        gbCounter.Controls.Add(NewInlineLabel("Start:", 10, 27));
        _numAdvStart = new NumericUpDown { Location = new Point(52, 24), Width = 78, Minimum = 0, Maximum = 1_000_000_000, Value = 1 };
        gbCounter.Controls.Add(NewInlineLabel("Step:", 148, 27));
        _numAdvStep = new NumericUpDown { Location = new Point(184, 24), Width = 56, Minimum = 1, Maximum = 1_000_000, Value = 1 };
        gbCounter.Controls.Add(NewInlineLabel("Padding:", 258, 27));
        _numAdvPad = new NumericUpDown { Location = new Point(314, 24), Width = 54, Minimum = 0, Maximum = 12, Value = 3 };
        gbCounter.Controls.Add(NewInlineLabel("Style:", 10, 55));
        _cmbCounterStyle = new ComboBox { Location = new Point(52, 52), Width = 172, DropDownStyle = ComboBoxStyle.DropDownList };
        // Order mirrors the CounterStyle enum.
        _cmbCounterStyle.Items.AddRange(["1, 2, 3 (numeric)", "a, b, c", "A, B, C", "i, ii, iii (roman)", "I, II, III (Roman)", "hex — 1f, 20", "HEX — 1F, 20"]);
        _cmbCounterStyle.SelectedIndex = 0;
        _tips.SetToolTip(_cmbCounterStyle, "How counter values are rendered — applies to both {counter} and {counter2}.");
        _chkCounterPerFolder = new CheckBox { Text = "Restart the counter in each subfolder", Location = new Point(10, 82), AutoSize = true };
        gbCounter.Controls.AddRange([_numAdvStart, _numAdvStep, _numAdvPad, _cmbCounterStyle, _chkCounterPerFolder]);

        var gbCounter2 = NewAdvGroup("Counter 2 — {counter2}", 58);
        gbCounter2.Controls.Add(NewInlineLabel("Start:", 10, 27));
        _numCounter2Start = new NumericUpDown { Location = new Point(52, 24), Width = 78, Minimum = 0, Maximum = 1_000_000_000, Value = 1 };
        gbCounter2.Controls.Add(NewInlineLabel("Step:", 148, 27));
        _numCounter2Step = new NumericUpDown { Location = new Point(184, 24), Width = 56, Minimum = 1, Maximum = 1_000_000, Value = 1 };
        gbCounter2.Controls.Add(NewInlineLabel("Padding:", 258, 27));
        _numCounter2Pad = new NumericUpDown { Location = new Point(314, 24), Width = 54, Minimum = 0, Maximum = 12, Value = 2 };
        _tips.SetToolTip(gbCounter2, "An independent second counter with its own start, step, and padding.");
        gbCounter2.Controls.AddRange([_numCounter2Start, _numCounter2Step, _numCounter2Pad]);

        var gbOrder = NewAdvGroup("Numbering order", 60);
        gbOrder.Controls.Add(NewInlineLabel("Assign counters:", 10, 27));
        _cmbNumberingOrder = new ComboBox { Location = new Point(112, 24), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        // Order mirrors the NumberingOrder enum.
        _cmbNumberingOrder.Items.AddRange(["As listed", "Name A → Z", "Name Z → A", "Date, oldest first",
            "Date, newest first", "Size, smallest first", "Size, largest first", "Full path A → Z"]);
        _cmbNumberingOrder.SelectedIndex = 0;
        _tips.SetToolTip(_cmbNumberingOrder, "Which order files receive counter values — the preview grid order itself doesn't change.");
        gbOrder.Controls.Add(_cmbNumberingOrder);

        stack.Controls.AddRange([gbPattern, gbCounter, gbCounter2, gbOrder]);

        WireChange(_txtAdvPattern, _cmbDateSource,
            _numAdvStart, _numAdvStep, _numAdvPad, _cmbCounterStyle, _chkCounterPerFolder,
            _numCounter2Start, _numCounter2Step, _numCounter2Pad, _cmbNumberingOrder);
    }

    // ---------- Tab 2: Find & replace ----------

    private void BuildFindReplaceTab(TabControl tabs)
    {
        var stack = NewTabStack(tabs, "Find & replace");

        var gbRules = NewAdvGroup("Rules — applied top to bottom", 320);
        _rulesGrid = new DataGridView
        {
            Location = new Point(10, 22),
            Size = new Size(448, 218),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "On", Width = 30 });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Find", Width = 104 });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Replace", Width = 98 });
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Rx", Width = 28, ToolTipText = "Regular expression" });
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Aa", Width = 28, ToolTipText = "Case-sensitive" });
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "W", Width = 34, ToolTipText = "Whole words only (plain-text rules; regex rules ignore this)" });
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "1st", Width = 30, ToolTipText = "Replace only the first occurrence" });
        var colTarget = new DataGridViewComboBoxColumn
        {
            HeaderText = "Target",
            Width = 72,
            FlatStyle = FlatStyle.Flat,
            ToolTipText = "Which part of the file name this rule touches",
        };
        colTarget.Items.AddRange("Name", "Ext", "Both");
        _rulesGrid.Columns.Add(colTarget);
        foreach (DataGridViewColumn col in _rulesGrid.Columns)
            col.SortMode = DataGridViewColumnSortMode.NotSortable;

        _rulesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_rulesGrid.IsCurrentCellDirty
                && _rulesGrid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
                _rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _rulesGrid.CellValueChanged += (_, _) => { ValidateRules(); RequestPreview(); };
        _rulesGrid.RowsRemoved += (_, _) => { ValidateRules(); RequestPreview(); };
        // A stray value in the Target combo column must never crash the form.
        _rulesGrid.DataError += (_, e) => e.ThrowException = false;

        var btnAdd = new Button { Text = "Add", Location = new Point(10, 248), Width = 70 };
        var btnRemove = new Button { Text = "Remove", Location = new Point(84, 248), Width = 70 };
        var btnUp = new Button { Text = "Up", Location = new Point(170, 248), Width = 60 };
        var btnDown = new Button { Text = "Down", Location = new Point(234, 248), Width = 60 };
        btnAdd.Click += (_, _) =>
        {
            _rulesGrid.Rows.Add(true, "", "", false, false, false, false, "Name");
            _rulesGrid.CurrentCell = _rulesGrid.Rows[^1].Cells[RuleColFind];
            RequestPreview();
        };
        btnRemove.Click += (_, _) =>
        {
            var index = _rulesGrid.CurrentCell?.RowIndex ?? -1;
            if (index >= 0) _rulesGrid.Rows.RemoveAt(index);
        };
        btnUp.Click += (_, _) => MoveRule(-1);
        btnDown.Click += (_, _) => MoveRule(+1);

        _lblRulesStatus = new Label
        {
            Location = new Point(10, 280),
            Size = new Size(448, 32),
            ForeColor = SystemColors.GrayText,
            Text = "No rules — add one to start replacing.",
        };
        gbRules.Controls.AddRange([_rulesGrid, btnAdd, btnRemove, btnUp, btnDown, _lblRulesStatus]);

        var gbSwap = NewAdvGroup("Swap around a separator", 84);
        _chkSwap = new CheckBox { Text = "Swap the two halves around:", Location = new Point(10, 24), AutoSize = true };
        _txtSwapSeparator = new TextBox { Location = new Point(196, 22), Width = 80, Text = " - " };
        var swapHint = new Label
        {
            Text = "\"Artist - Title\" becomes \"Title - Artist\" (splits on the first occurrence).",
            Location = new Point(10, 52),
            Size = new Size(448, 18),
            ForeColor = SystemColors.GrayText,
        };
        gbSwap.Controls.AddRange([_chkSwap, _txtSwapSeparator, swapHint]);

        stack.Controls.AddRange([gbRules, gbSwap]);

        WireChange(_chkSwap, _txtSwapSeparator);
    }

    private void MoveRule(int delta)
    {
        var index = _rulesGrid.CurrentCell?.RowIndex ?? -1;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _rulesGrid.Rows.Count) return;
        var colIndex = _rulesGrid.CurrentCell!.ColumnIndex;
        var row = _rulesGrid.Rows[index];
        _rulesGrid.Rows.RemoveAt(index);
        _rulesGrid.Rows.Insert(target, row);
        _rulesGrid.CurrentCell = _rulesGrid.Rows[target].Cells[colIndex];
        ValidateRules();
        RequestPreview();
    }

    /// <summary>Tints invalid regex Find cells red and summarizes rule health below the grid.</summary>
    private void ValidateRules()
    {
        int invalid = 0;
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            var findCell = row.Cells[RuleColFind];
            var find = findCell.Value as string ?? "";
            var useRegex = row.Cells[RuleColRegex].Value is true;
            string? error = null;
            if (useRegex && find.Length > 0)
            {
                try { _ = new Regex(find); }
                catch (Exception ex) { error = ex.Message; }
            }
            if (error is null)
            {
                findCell.Style.BackColor = Color.Empty;
                findCell.ToolTipText = "";
            }
            else
            {
                findCell.Style.BackColor = ThemeService.GridProblemBack;
                findCell.ToolTipText = error;
                invalid++;
            }
        }
        _lblRulesStatus.ForeColor = invalid > 0 ? ThemeService.GridProblemText : SystemColors.GrayText;
        _lblRulesStatus.Text = invalid > 0
            ? $"{invalid} rule(s) have an invalid regular expression — hover the red cell."
            : _rulesGrid.Rows.Count == 0
                ? "No rules — add one to start replacing."
                : $"{_rulesGrid.Rows.Count} rule(s), applied in order.";
    }

    // ---------- scope sync with the bar above the grid ----------

    private void SyncScopeToAdvanced()
    {
        if (_syncingScope || _txtAdvIncludeMask is null) return;
        _syncingScope = true;
        _txtAdvIncludeMask.Text = _txtIncludeMask.Text;
        _txtAdvExcludeMask.Text = _txtExcludeMask.Text;
        _chkAdvSubfolders.Checked = _chkSubfolders.Checked;
        _syncingScope = false;
    }

    private void SyncScopeFromAdvanced()
    {
        if (_syncingScope) return;
        _syncingScope = true;
        _txtIncludeMask.Text = _txtAdvIncludeMask.Text;
        _txtExcludeMask.Text = _txtAdvExcludeMask.Text;
        _chkSubfolders.Checked = _chkAdvSubfolders.Checked;
        _syncingScope = false;
        RequestPreview();
    }
}
