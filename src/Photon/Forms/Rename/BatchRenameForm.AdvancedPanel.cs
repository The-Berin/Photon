using System.Text.RegularExpressions;

namespace Photon.App.Forms;

public partial class BatchRenameForm
{
    private TextBox _txtAdvPattern = null!;
    private ComboBox _cmbDateSource = null!;

    private NumericUpDown _numAdvStart = null!;
    private NumericUpDown _numAdvStep = null!;
    private NumericUpDown _numAdvPad = null!;
    private CheckBox _chkCounterPerFolder = null!;

    private DataGridView _rulesGrid = null!;
    private Label _lblRulesStatus = null!;
    private const int RuleColOn = 0, RuleColFind = 1, RuleColReplace = 2, RuleColRegex = 3, RuleColCase = 4;

    private CheckBox _chkRemoveRange = null!;
    private NumericUpDown _numRemoveStart = null!;
    private NumericUpDown _numRemoveCount = null!;
    private CheckBox _chkRemoveFromEnd = null!;
    private CheckBox _chkRemoveNumbers = null!;
    private CheckBox _chkRemoveBrackets = null!;

    private CheckBox _chkInsert = null!;
    private TextBox _txtInsertText = null!;
    private NumericUpDown _numInsertPos = null!;
    private CheckBox _chkInsertFromEnd = null!;

    private CheckBox _chkTrim = null!;
    private CheckBox _chkCollapse = null!;
    private CheckBox _chkDiacritics = null!;
    private TextBox _txtStrip = null!;
    private CheckBox _chkReplaceSpaces = null!;
    private TextBox _txtReplaceSpacesWith = null!;

    private ComboBox _cmbAdvNameCase = null!;
    private ComboBox _cmbAdvExtCase = null!;

    private TextBox _txtAdvPrefix = null!;
    private TextBox _txtAdvSuffix = null!;

    private RadioButton _radConflictAppend = null!;
    private RadioButton _radConflictSkip = null!;
    private RadioButton _radConflictFail = null!;

    private TextBox _txtAdvIncludeMask = null!;
    private TextBox _txtAdvExcludeMask = null!;
    private CheckBox _chkAdvSubfolders = null!;

    private void BuildAdvancedPanel()
    {
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4),
        };

        // ----- 1. Pattern & date -----
        var gbPattern = NewGroup("Pattern && date source", 86);
        _txtAdvPattern = new TextBox { Location = new Point(10, 24), Width = 320, Text = "{name}" };
        var btnToken = new Button { Text = "Insert token ▾", Location = new Point(336, 22), Width = 100 };
        var tokenMenu = TokenCatalog.BuildInsertMenu(_txtAdvPattern);
        btnToken.Click += (_, _) => tokenMenu.Show(btnToken, new Point(0, btnToken.Height));
        gbPattern.Controls.Add(NewInlineLabel("Date tokens use:", 10, 57));
        _cmbDateSource = new ComboBox
        {
            Location = new Point(112, 53), Width = 218, DropDownStyle = ComboBoxStyle.DropDownList,
        };
        // Order mirrors the DateSource enum.
        _cmbDateSource.Items.AddRange(["EXIF date, then file date", "EXIF date only", "File date, then EXIF", "File date only"]);
        _cmbDateSource.SelectedIndex = 0;
        gbPattern.Controls.AddRange([_txtAdvPattern, btnToken, _cmbDateSource]);

        // ----- 2. Counter -----
        var gbCounter = NewGroup("Counter — {counter}", 84);
        gbCounter.Controls.Add(NewInlineLabel("Start:", 10, 27));
        _numAdvStart = new NumericUpDown { Location = new Point(50, 24), Width = 78, Minimum = 0, Maximum = 1_000_000_000, Value = 1 };
        gbCounter.Controls.Add(NewInlineLabel("Step:", 146, 27));
        _numAdvStep = new NumericUpDown { Location = new Point(182, 24), Width = 58, Minimum = 1, Maximum = 1_000_000, Value = 1 };
        gbCounter.Controls.Add(NewInlineLabel("Padding:", 258, 27));
        _numAdvPad = new NumericUpDown { Location = new Point(314, 24), Width = 54, Minimum = 0, Maximum = 12, Value = 3 };
        _chkCounterPerFolder = new CheckBox { Text = "Restart the counter in each subfolder", Location = new Point(10, 54), AutoSize = true };
        gbCounter.Controls.AddRange([_numAdvStart, _numAdvStep, _numAdvPad, _chkCounterPerFolder]);

        // ----- 3. Find & replace rules -----
        var gbRules = NewGroup("Find && replace rules — applied top to bottom", 244);
        _rulesGrid = new DataGridView
        {
            Location = new Point(10, 22),
            Size = new Size(348, 186),
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
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "On", Width = 32 });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Find", Width = 116 });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Replace", Width = 108 });
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Rx", Width = 32, ToolTipText = "Regular expression" });
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Aa", Width = 32, ToolTipText = "Case-sensitive" });
        foreach (DataGridViewColumn col in _rulesGrid.Columns)
            col.SortMode = DataGridViewColumnSortMode.NotSortable;

        _rulesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_rulesGrid.IsCurrentCellDirty && _rulesGrid.CurrentCell is DataGridViewCheckBoxCell)
                _rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _rulesGrid.CellValueChanged += (_, _) => { ValidateRules(); RequestPreview(); };
        _rulesGrid.RowsRemoved += (_, _) => { ValidateRules(); RequestPreview(); };

        var btnAdd = new Button { Text = "Add", Location = new Point(366, 22), Width = 70 };
        var btnRemove = new Button { Text = "Remove", Location = new Point(366, 50), Width = 70 };
        var btnUp = new Button { Text = "Up", Location = new Point(366, 86), Width = 70 };
        var btnDown = new Button { Text = "Down", Location = new Point(366, 114), Width = 70 };
        btnAdd.Click += (_, _) =>
        {
            _rulesGrid.Rows.Add(true, "", "", false, false);
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
            Location = new Point(10, 214),
            Size = new Size(426, 20),
            ForeColor = SystemColors.GrayText,
            Text = "No rules — add one to start replacing.",
        };
        gbRules.Controls.AddRange([_rulesGrid, btnAdd, btnRemove, btnUp, btnDown, _lblRulesStatus]);

        // ----- 4. Remove -----
        var gbRemove = NewGroup("Remove", 112);
        _chkRemoveRange = new CheckBox { Text = "Remove range —", Location = new Point(10, 24), AutoSize = true };
        gbRemove.Controls.Add(NewInlineLabel("at position:", 128, 26));
        _numRemoveStart = new NumericUpDown { Location = new Point(196, 22), Width = 54, Minimum = 0, Maximum = 9999 };
        gbRemove.Controls.Add(NewInlineLabel("count:", 258, 26));
        _numRemoveCount = new NumericUpDown { Location = new Point(300, 22), Width = 54, Minimum = 0, Maximum = 9999 };
        _chkRemoveFromEnd = new CheckBox { Text = "from end", Location = new Point(362, 24), AutoSize = true };
        _chkRemoveNumbers = new CheckBox { Text = "Remove all digits 0-9", Location = new Point(10, 54), AutoSize = true };
        _chkRemoveBrackets = new CheckBox { Text = "Remove (bracketed) and [bracketed] text", Location = new Point(10, 80), AutoSize = true };
        gbRemove.Controls.AddRange([_chkRemoveRange, _numRemoveStart, _numRemoveCount, _chkRemoveFromEnd,
            _chkRemoveNumbers, _chkRemoveBrackets]);

        // ----- 5. Insert -----
        var gbInsert = NewGroup("Insert text", 60);
        _chkInsert = new CheckBox { Text = "Insert", Location = new Point(10, 24), AutoSize = true };
        _txtInsertText = new TextBox { Location = new Point(72, 22), Width = 140 };
        gbInsert.Controls.Add(NewInlineLabel("at position:", 220, 26));
        _numInsertPos = new NumericUpDown { Location = new Point(288, 22), Width = 54, Minimum = 0, Maximum = 9999 };
        _chkInsertFromEnd = new CheckBox { Text = "from end", Location = new Point(350, 24), AutoSize = true };
        gbInsert.Controls.AddRange([_chkInsert, _txtInsertText, _numInsertPos, _chkInsertFromEnd]);

        // ----- 6. Hygiene -----
        var gbHygiene = NewGroup("Name hygiene", 138);
        _chkTrim = new CheckBox { Text = "Trim leading/trailing whitespace", Location = new Point(10, 22), AutoSize = true, Checked = true };
        _chkCollapse = new CheckBox { Text = "Collapse repeated spaces", Location = new Point(232, 22), AutoSize = true };
        _chkDiacritics = new CheckBox { Text = "Remove diacritics (é → e)", Location = new Point(10, 48), AutoSize = true };
        gbHygiene.Controls.Add(NewInlineLabel("Strip characters:", 10, 80));
        _txtStrip = new TextBox { Location = new Point(112, 77), Width = 120 };
        var tips = new ToolTip();
        tips.SetToolTip(_txtStrip, "Every character typed here is deleted from names, e.g. #&!");
        _chkReplaceSpaces = new CheckBox { Text = "Replace spaces with:", Location = new Point(10, 106), AutoSize = true };
        _txtReplaceSpacesWith = new TextBox { Location = new Point(148, 103), Width = 84, Text = "_", Enabled = false };
        _chkReplaceSpaces.CheckedChanged += (_, _) => _txtReplaceSpacesWith.Enabled = _chkReplaceSpaces.Checked;
        gbHygiene.Controls.AddRange([_chkTrim, _chkCollapse, _chkDiacritics, _txtStrip, _chkReplaceSpaces, _txtReplaceSpacesWith]);

        // ----- 7. Case -----
        var gbCase = NewGroup("Case transforms", 60);
        gbCase.Controls.Add(NewInlineLabel("Name:", 10, 27));
        _cmbAdvNameCase = NewCaseCombo(56, 24, 156);
        gbCase.Controls.Add(NewInlineLabel("Extension:", 226, 27));
        _cmbAdvExtCase = NewCaseCombo(292, 24, 144);
        gbCase.Controls.AddRange([_cmbAdvNameCase, _cmbAdvExtCase]);

        // ----- 8. Prefix / suffix -----
        var gbAffix = NewGroup("Prefix / suffix", 60);
        gbAffix.Controls.Add(NewInlineLabel("Prefix:", 10, 27));
        _txtAdvPrefix = new TextBox { Location = new Point(56, 24), Width = 156 };
        gbAffix.Controls.Add(NewInlineLabel("Suffix:", 226, 27));
        _txtAdvSuffix = new TextBox { Location = new Point(272, 24), Width = 164 };
        gbAffix.Controls.AddRange([_txtAdvPrefix, _txtAdvSuffix]);

        // ----- 9. Conflicts -----
        var gbConflict = NewGroup("When the new name already exists", 100);
        _radConflictAppend = new RadioButton { Text = "Append a number (photo.jpg → photo_1.jpg)", Location = new Point(10, 22), AutoSize = true, Checked = true };
        _radConflictSkip = new RadioButton { Text = "Skip that file", Location = new Point(10, 46), AutoSize = true };
        _radConflictFail = new RadioButton { Text = "Mark it as a problem and don't rename it", Location = new Point(10, 70), AutoSize = true };
        gbConflict.Controls.AddRange([_radConflictAppend, _radConflictSkip, _radConflictFail]);

        // ----- 10. Scope -----
        var gbScope = NewGroup("Scope — mirrors the bar above the grid", 112);
        gbScope.Controls.Add(NewInlineLabel("Include mask:", 10, 26));
        _txtAdvIncludeMask = new TextBox { Location = new Point(98, 23), Width = 338 };
        gbScope.Controls.Add(NewInlineLabel("Exclude mask:", 10, 54));
        _txtAdvExcludeMask = new TextBox { Location = new Point(98, 51), Width = 338 };
        _chkAdvSubfolders = new CheckBox { Text = "Include subfolders (applies on next Load)", Location = new Point(10, 80), AutoSize = true };
        gbScope.Controls.AddRange([_txtAdvIncludeMask, _txtAdvExcludeMask, _chkAdvSubfolders]);
        _txtAdvIncludeMask.TextChanged += (_, _) => SyncScopeFromAdvanced();
        _txtAdvExcludeMask.TextChanged += (_, _) => SyncScopeFromAdvanced();
        _chkAdvSubfolders.CheckedChanged += (_, _) => SyncScopeFromAdvanced();

        stack.Controls.AddRange([gbPattern, gbCounter, gbRules, gbRemove, gbInsert, gbHygiene,
            gbCase, gbAffix, gbConflict, gbScope]);
        _advancedHost.Controls.Add(stack);

        WireChange(_txtAdvPattern, _cmbDateSource,
            _numAdvStart, _numAdvStep, _numAdvPad, _chkCounterPerFolder,
            _chkRemoveRange, _numRemoveStart, _numRemoveCount, _chkRemoveFromEnd, _chkRemoveNumbers, _chkRemoveBrackets,
            _chkInsert, _txtInsertText, _numInsertPos, _chkInsertFromEnd,
            _chkTrim, _chkCollapse, _chkDiacritics, _txtStrip, _chkReplaceSpaces, _txtReplaceSpacesWith,
            _cmbAdvNameCase, _cmbAdvExtCase, _txtAdvPrefix, _txtAdvSuffix,
            _radConflictAppend, _radConflictSkip, _radConflictFail);
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
                findCell.Style.BackColor = Color.FromArgb(248, 215, 218);
                findCell.ToolTipText = error;
                invalid++;
            }
        }
        _lblRulesStatus.ForeColor = invalid > 0 ? Color.FromArgb(150, 30, 30) : SystemColors.GrayText;
        _lblRulesStatus.Text = invalid > 0
            ? $"{invalid} rule(s) have an invalid regular expression — hover the red cell."
            : _rulesGrid.Rows.Count == 0
                ? "No rules — add one to start replacing."
                : $"{_rulesGrid.Rows.Count} rule(s), applied in order.";
    }

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
