namespace Photon.App.Forms;

public partial class BatchRenameForm
{
    private const int ColInclude = 0, ColOld = 1, ColNew = 2, ColFolder = 3, ColSize = 4, ColStatus = 5;

    /// <summary>The one ToolTip component every panel shares.</summary>
    private ToolTip _tips = null!;

    private TableLayoutPanel _topPanel = null!;
    private TextBox _txtFolder = null!;
    private Button _btnBrowse = null!;
    private CheckBox _chkSubfolders = null!;
    private CheckBox _chkAllFiles = null!;
    private Button _btnLoad = null!;
    private TextBox _txtIncludeMask = null!;
    private TextBox _txtExcludeMask = null!;

    private DataGridView _grid = null!;
    private Panel _optionsColumn = null!;
    private ToolStrip _presetStrip = null!;
    private ToolStripComboBox _cmbPreset = null!;
    private CheckBox _chkAdvancedToggle = null!;
    private Panel _simpleHost = null!;
    private Panel _advancedHost = null!;

    private Label _lblCounts = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progress = null!;
    private Button _btnRename = null!;
    private Button _btnCancel = null!;

    private void BuildLayout()
    {
        SuspendLayout();
        _tips = new ToolTip();

        // ----- top: folder + masks -----
        _topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 66,
            ColumnCount = 6,
            RowCount = 2,
            Padding = new Padding(8, 6, 8, 2),
        };
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _txtFolder = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 3) };
        _btnBrowse = new Button { Text = "Browse…", AutoSize = true };
        _btnBrowse.Click += (_, _) => BrowseFolder();
        _chkSubfolders = new CheckBox { Text = "Include subfolders", AutoSize = true, Anchor = AnchorStyles.Left };
        _chkAllFiles = new CheckBox { Text = "All files (not just media)", AutoSize = true, Anchor = AnchorStyles.Left };
        _btnLoad = new Button { Text = "Load", AutoSize = true };
        _btnLoad.Click += (_, _) => _ = LoadFilesAsync();

        _topPanel.Controls.Add(NewLabel("Folder:"), 0, 0);
        _topPanel.Controls.Add(_txtFolder, 1, 0);
        _topPanel.Controls.Add(_btnBrowse, 2, 0);
        _topPanel.Controls.Add(_chkSubfolders, 3, 0);
        _topPanel.Controls.Add(_chkAllFiles, 4, 0);
        _topPanel.Controls.Add(_btnLoad, 5, 0);

        var maskRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            Margin = new Padding(0),
        };
        maskRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        maskRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        maskRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        maskRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _txtIncludeMask = new TextBox { Dock = DockStyle.Fill };
        _txtExcludeMask = new TextBox { Dock = DockStyle.Fill };
        _tips.SetToolTip(_txtIncludeMask, "Wildcard mask like IMG_*.jpg — empty means all loaded files");
        _tips.SetToolTip(_txtExcludeMask, "Files matching this mask are left alone");
        maskRow.Controls.Add(NewLabel("Include mask:"), 0, 0);
        maskRow.Controls.Add(_txtIncludeMask, 1, 0);
        maskRow.Controls.Add(NewLabel("Exclude mask:"), 2, 0);
        maskRow.Controls.Add(_txtExcludeMask, 3, 0);
        _topPanel.Controls.Add(maskRow, 0, 1);
        _topPanel.SetColumnSpan(maskRow, 6);

        WireChange(_txtIncludeMask, _txtExcludeMask, _chkSubfolders);
        _txtIncludeMask.TextChanged += (_, _) => SyncScopeToAdvanced();
        _txtExcludeMask.TextChanged += (_, _) => SyncScopeToAdvanced();
        _chkSubfolders.CheckedChanged += (_, _) => SyncScopeToAdvanced();

        // ----- bottom bar -----
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8, 6, 8, 6) };
        _lblCounts = new Label { AutoSize = true, Location = new Point(10, 12), Text = "0 files" };
        _btnRename = new Button
        {
            Text = "Rename",
            Width = 110,
            Height = 30,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Enabled = false,
        };
        _btnRename.Click += OnRenameClick;
        _btnCancel = new Button
        {
            Text = "Cancel",
            Width = 90,
            Height = 30,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Visible = false,
        };
        _btnCancel.Click += (_, _) => { _cts?.Cancel(); _lblStatus.Text = "Cancelling…"; };
        _progress = new ProgressBar
        {
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Width = 260,
            Height = 22,
            Visible = false,
        };
        _lblStatus = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Width = 420,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
        };
        bottom.Controls.AddRange([_lblCounts, _lblStatus, _progress, _btnCancel, _btnRename]);
        bottom.Resize += (_, _) =>
        {
            _btnRename.Location = new Point(bottom.ClientSize.Width - _btnRename.Width - 10, 6);
            _btnCancel.Location = new Point(_btnRename.Left - _btnCancel.Width - 6, 6);
            _progress.Location = new Point(_btnCancel.Left - _progress.Width - 10, 10);
            _lblStatus.Location = new Point(_lblCounts.Right + 24, 10);
            _lblStatus.Width = Math.Max(80, _progress.Left - _lblStatus.Left - 10);
        };

        // ----- right: options column -----
        _optionsColumn = new Panel { Dock = DockStyle.Right, Width = 520, Padding = new Padding(4, 0, 0, 0) };

        _presetStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        _cmbPreset = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        _cmbPreset.SelectedIndexChanged += (_, _) => OnPresetSelected();
        var btnSavePreset = new ToolStripButton("Save…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnSavePreset.Click += (_, _) => SavePreset();
        var btnDeletePreset = new ToolStripButton("Delete") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btnDeletePreset.Click += (_, _) => DeletePreset();
        _presetStrip.Items.AddRange(
        [
            new ToolStripLabel("Preset:"),
            _cmbPreset,
            btnSavePreset,
            btnDeletePreset,
        ]);

        _chkAdvancedToggle = new CheckBox
        {
            Appearance = Appearance.Button,
            Text = "Advanced mode — full control center",
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font(Font, FontStyle.Bold),
        };
        _chkAdvancedToggle.CheckedChanged += (_, _) => SetMode(_chkAdvancedToggle.Checked);

        _simpleHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(2) };
        _advancedHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(2), Visible = false };

        _optionsColumn.Controls.Add(_simpleHost);
        _optionsColumn.Controls.Add(_advancedHost);
        _optionsColumn.Controls.Add(_chkAdvancedToggle);
        _optionsColumn.Controls.Add(_presetStrip);
        _simpleHost.BringToFront();
        _advancedHost.BringToFront();

        // ----- center: preview grid -----
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Width = 30, Resizable = DataGridViewTriState.False });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Old name", Width = 250, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "New name", Width = 250, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Folder",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120,
            ReadOnly = true,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Size",
            Width = 90,
            ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", Width = 170, ReadOnly = true });
        foreach (DataGridViewColumn col in _grid.Columns)
            col.SortMode = DataGridViewColumnSortMode.NotSortable;

        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.CellValuePushed += OnCellValuePushed;
        _grid.CellFormatting += OnCellFormatting;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        var gridMenu = new ContextMenuStrip();
        gridMenu.Items.Add("Check all", null, (_, _) => SetAllIncluded(_ => true));
        gridMenu.Items.Add("Uncheck all", null, (_, _) => SetAllIncluded(_ => false));
        gridMenu.Items.Add("Invert checks", null, (_, _) => SetAllIncluded(r => !r.Included));
        gridMenu.Items.Add(new ToolStripSeparator());
        gridMenu.Items.Add("Check selected rows only", null, (_, _) =>
        {
            var selected = new HashSet<int>();
            foreach (DataGridViewRow r in _grid.SelectedRows) selected.Add(r.Index);
            for (int i = 0; i < _rows.Count; i++) _rows[i].Included = selected.Contains(i);
            _grid.Invalidate();
            RequestPreview();
        });
        _grid.ContextMenuStrip = gridMenu;

        Controls.Add(_grid);
        Controls.Add(_optionsColumn);
        Controls.Add(bottom);
        Controls.Add(_topPanel);
        _grid.BringToFront();

        ResumeLayout();
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Folder to rename files in",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_txtFolder.Text) ? _txtFolder.Text : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtFolder.Text = dlg.SelectedPath;
            _ = LoadFilesAsync();
        }
    }

    // ---------- presets ----------

    private void RefreshPresetCombo(string? select = null)
    {
        _suspendPreview = true;
        _cmbPreset.Items.Clear();
        _cmbPreset.Items.Add("(current settings)");
        foreach (var name in RenamePresetStore.BuiltIn.Keys) _cmbPreset.Items.Add("★ " + name);
        foreach (var name in _presets.User.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            _cmbPreset.Items.Add(name);
        var index = select is null ? 0 : _cmbPreset.Items.IndexOf(select);
        _cmbPreset.SelectedIndex = index < 0 ? 0 : index;
        _suspendPreview = false;
    }

    private static string PresetNameFromItem(string item) => item.StartsWith("★ ") ? item[2..] : item;

    private void OnPresetSelected()
    {
        if (_suspendPreview || _cmbPreset.SelectedIndex <= 0) return;
        var name = PresetNameFromItem((string)_cmbPreset.SelectedItem!);
        var options = _presets.Get(name);
        if (options is null) return;
        // Presets carry the full option set, so show them on the full panel.
        if (!_advancedMode) _chkAdvancedToggle.Checked = true;
        ApplyOptionsToControls(options);
    }

    private void SavePreset()
    {
        var current = _cmbPreset.SelectedIndex > 0 ? PresetNameFromItem((string)_cmbPreset.SelectedItem!) : "";
        using var dlg = new TextPromptDialog("Save preset", "Preset name:",
            _presets.IsBuiltIn(current) ? "" : current);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Value.Length == 0) return;
        if (_presets.IsBuiltIn(dlg.Value))
        {
            MessageBox.Show(this, "That name belongs to a built-in preset. Pick another.", "Save preset",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _presets.User[dlg.Value] = CollectOptions();
        try { _presets.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save presets: {ex.Message}", "Save preset",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        RefreshPresetCombo(dlg.Value);
    }

    private void DeletePreset()
    {
        if (_cmbPreset.SelectedIndex <= 0) return;
        var name = PresetNameFromItem((string)_cmbPreset.SelectedItem!);
        if (_presets.IsBuiltIn(name))
        {
            MessageBox.Show(this, "Built-in presets can't be deleted.", "Delete preset",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"Delete preset \"{name}\"?", "Delete preset",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _presets.User.Remove(name);
        try { _presets.Save(); } catch { }
        RefreshPresetCombo();
    }

    // ---------- shared helpers ----------

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    /// <summary>Re-plans the preview whenever any of the given option controls change.</summary>
    private void WireChange(params Control[] controls)
    {
        foreach (var c in controls)
        {
            switch (c)
            {
                case TextBox t: t.TextChanged += (_, _) => RequestPreview(); break;
                case CheckBox k: k.CheckedChanged += (_, _) => RequestPreview(); break;
                case RadioButton r: r.CheckedChanged += (_, _) => RequestPreview(); break;
                case ComboBox b: b.SelectedIndexChanged += (_, _) => RequestPreview(); break;
                case NumericUpDown n: n.ValueChanged += (_, _) => RequestPreview(); break;
                // Fires for the embedded null-checkbox too — the native control raises
                // DTN_DATETIMECHANGE (hence ValueChanged) when the check state flips.
                case DateTimePicker d: d.ValueChanged += (_, _) => RequestPreview(); break;
            }
        }
    }
}
