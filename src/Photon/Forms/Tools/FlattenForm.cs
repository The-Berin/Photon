using Photon.App.Services;
using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.App.Forms;

/// <summary>
/// Feature 3: folder flattener — pulls every (media) file in a tree up to the root folder,
/// with a full from→to preview before anything moves. Journaled and undoable from History.
/// </summary>
public sealed class FlattenForm : Form
{
    private readonly TextBox _folderBox = new();
    private readonly Button _browseButton = new() { Text = "Browse...", AutoSize = true };
    private readonly CheckBox _mediaOnlyCheck = new() { Text = "Media files only (pictures and videos)", AutoSize = true };
    private readonly CheckBox _removeEmptyCheck = new() { Text = "Remove folders left empty", AutoSize = true, Checked = true };
    private readonly RadioButton _conflictNumber = new() { Text = "Append number (_1, _2, ...)", AutoSize = true, Checked = true };
    private readonly RadioButton _conflictFolder = new() { Text = "Append folder name", AutoSize = true };
    private readonly RadioButton _conflictSkip = new() { Text = "Skip the file", AutoSize = true };
    private readonly Button _previewButton = new() { Text = "Preview", AutoSize = true };
    private readonly Button _flattenButton = new() { Text = "Flatten", AutoSize = true, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly ProgressBar _progressBar = new() { Width = 220, Anchor = AnchorStyles.Left };
    private readonly Label _statusLabel = new()
    {
        Text = "Pick a folder, then Preview to see every move before it happens.",
        AutoSize = false, Height = 20, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
    };
    private readonly ListView _grid = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowItemToolTips = true,
    };

    private FlattenPlan? _plan;
    private FlattenOptions? _planOptions;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public FlattenForm(string? initialFolder = null)
    {
        Text = "Flatten Folder";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 640);
        MinimumSize = new Size(820, 480);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // folder row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // options group
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons + progress
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // grid

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _folderBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        top.Controls.Add(_folderBox, 1, 0);
        top.Controls.Add(_browseButton, 2, 0);
        root.Controls.Add(top, 0, 0);

        var optionsGroup = new GroupBox { Text = "Options", Dock = DockStyle.Fill, Height = 106 };
        _mediaOnlyCheck.Location = new Point(10, 22);
        _removeEmptyCheck.Location = new Point(10, 46);
        optionsGroup.Controls.Add(new Label { Text = "When names collide:", AutoSize = true, Location = new Point(10, 76) });
        _conflictNumber.Location = new Point(140, 74);
        _conflictFolder.Location = new Point(330, 74);
        _conflictSkip.Location = new Point(500, 74);
        optionsGroup.Controls.AddRange([_mediaOnlyCheck, _removeEmptyCheck, _conflictNumber, _conflictFolder, _conflictSkip]);
        root.Controls.Add(optionsGroup, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        actions.Controls.AddRange([_previewButton, _flattenButton, _cancelButton, _progressBar]);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(_statusLabel, 0, 3);

        _grid.Columns.Add("From", 460);
        _grid.Columns.Add("To", 420);
        root.Controls.Add(_grid, 0, 4);

        Controls.Add(root);

        _browseButton.Click += OnBrowse;
        _previewButton.Click += OnPreview;
        _flattenButton.Click += OnFlatten;
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        FormClosing += (_, _) => _cts?.Cancel();

        // Any option change makes an existing preview stale.
        _folderBox.TextChanged += (_, _) => InvalidatePlan();
        _mediaOnlyCheck.CheckedChanged += (_, _) => InvalidatePlan();
        _removeEmptyCheck.CheckedChanged += (_, _) => InvalidatePlan();
        _conflictNumber.CheckedChanged += (_, _) => InvalidatePlan();
        _conflictFolder.CheckedChanged += (_, _) => InvalidatePlan();
        _conflictSkip.CheckedChanged += (_, _) => InvalidatePlan();

        if (!string.IsNullOrWhiteSpace(initialFolder)) _folderBox.Text = initialFolder.Trim();
        ThemeService.FixGaps(this);
    }

    private void InvalidatePlan()
    {
        if (_busy || _plan is null) return;
        _plan = null;
        _planOptions = null;
        _flattenButton.Enabled = false;
        _statusLabel.Text = "Options changed — build a new preview.";
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Choose the folder to flatten",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        var current = _folderBox.Text.Trim();
        if (current.Length > 0 && Directory.Exists(current)) dlg.SelectedPath = current;
        if (dlg.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dlg.SelectedPath;
    }

    private FlattenOptions? BuildOptions()
    {
        var folder = _folderBox.Text.Trim();
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            MessageBox.Show(this, "Pick an existing folder first.", "Flatten", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        return new FlattenOptions
        {
            Root = folder,
            MediaOnly = _mediaOnlyCheck.Checked,
            ConflictPolicy = _conflictFolder.Checked ? FlattenConflictPolicy.AppendFolderName
                : _conflictSkip.Checked ? FlattenConflictPolicy.Skip
                : FlattenConflictPolicy.AppendNumber,
            RemoveEmptyFolders = _removeEmptyCheck.Checked,
        };
    }

    private async void OnPreview(object? sender, EventArgs e)
    {
        if (_busy) return;
        var options = BuildOptions();
        if (options is null) return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        _statusLabel.Text = "Building preview…";
        try
        {
            IFolderFlattener flattener = new FolderFlattener();
            var plan = await Task.Run(() => flattener.BuildPlanAsync(options, _cts.Token), _cts.Token);

            _grid.BeginUpdate();
            _grid.Items.Clear();
            foreach (var item in plan.Items)
            {
                var row = new ListViewItem(item.SourcePath);
                row.SubItems.Add(item.DestinationPath);
                _grid.Items.Add(row);
            }
            _grid.EndUpdate();

            _plan = plan;
            _planOptions = options;
            _flattenButton.Enabled = plan.Items.Count > 0;
            _statusLabel.Text =
                $"{plan.Items.Count:N0} files will move · {plan.FoldersToRemove:N0} folders to remove" +
                (plan.Warnings.Count > 0 ? $" · {plan.Warnings.Count:N0} warnings" : "");
            if (plan.Warnings.Count > 0)
            {
                MessageBox.Show(this,
                    string.Join(Environment.NewLine, plan.Warnings.Take(12)) + (plan.Warnings.Count > 12 ? "\n…" : ""),
                    "Preview warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Preview cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Preview failed.";
            MessageBox.Show(this, ex.Message, "Flatten", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void OnFlatten(object? sender, EventArgs e)
    {
        if (_busy || _plan is null || _planOptions is null) return;
        var confirm = MessageBox.Show(this,
            $"Move {_plan.Items.Count:N0} files to the top of:\n{_planOptions.Root}\n\n" +
            "The operation is journaled and can be undone from History.",
            "Flatten", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            IFolderFlattener flattener = new FolderFlattener();
            var progress = new UiProgress(this, p =>
            {
                if (p.TotalCount > 0)
                    _progressBar.Value = Math.Clamp((int)(p.ProcessedCount * 100L / p.TotalCount), 0, 100);
                _statusLabel.Text = $"Moving {p.ProcessedCount:N0}/{p.TotalCount:N0} — {Path.GetFileName(p.CurrentFile)}";
            });
            var plan = _plan;
            var options = _planOptions;
            var result = await Task.Run(() => flattener.ExecuteAsync(plan, options, progress, _cts.Token), _cts.Token);

            var summary = $"Moved: {result.Moved:N0}\nSkipped: {result.Skipped:N0}\nEmpty folders removed: {result.FoldersRemoved:N0}";
            if (result.Errors.Count > 0)
            {
                summary += $"\nErrors: {result.Errors.Count:N0}";
                foreach (var (file, err) in result.Errors.Take(5))
                    summary += $"\n  {Path.GetFileName(file)}: {err}";
                if (result.Errors.Count > 5) summary += "\n  …";
            }
            summary += "\n\nUndo any time from History.";
            MessageBox.Show(this, summary, "Flatten complete", MessageBoxButtons.OK,
                result.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            _plan = null;
            _planOptions = null;
            _flattenButton.Enabled = false;
            _grid.Items.Clear();
            _statusLabel.Text = "Flatten finished — preview again to verify.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Flatten cancelled — completed moves are journaled and undoable from History.";
            _plan = null;
            _planOptions = null;
            _flattenButton.Enabled = false;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Flatten failed.";
            MessageBox.Show(this, ex.Message, "Flatten", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _previewButton.Enabled = !busy;
        _flattenButton.Enabled = !busy && _plan is { Items.Count: > 0 };
        _cancelButton.Enabled = busy;
        _browseButton.Enabled = !busy;
        _folderBox.Enabled = !busy;
        // Option changes during a build would be silently ignored (InvalidatePlan early-
        // returns on _busy), letting Flatten execute options that contradict the UI.
        _mediaOnlyCheck.Enabled = !busy;
        _removeEmptyCheck.Enabled = !busy;
        _conflictNumber.Enabled = !busy;
        _conflictFolder.Enabled = !busy;
        _conflictSkip.Enabled = !busy;
        if (!busy) _progressBar.Value = 0;
    }

    /// <summary>Marshals worker-thread progress onto the UI thread via BeginInvoke.</summary>
    private sealed class UiProgress(Control owner, Action<SortProgress> apply) : IProgress<SortProgress>
    {
        public void Report(SortProgress value)
        {
            if (owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke(() => { if (!owner.IsDisposed) apply(value); }); }
            catch (InvalidOperationException) { /* handle torn down mid-report */ }
        }
    }
}
