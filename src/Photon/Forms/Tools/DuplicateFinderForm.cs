using System.ComponentModel;
using Photon.App.Interop;
using Photon.App.Services;
using Photon.Core.Models;
using Photon.Core.Services;
using Photon.Core.Util;

namespace Photon.App.Forms;

/// <summary>
/// Feature 4: standalone duplicate checker with the full option set —
/// multi-folder scope, four compare modes, size/extension filters, keep policy,
/// and journaled resolution (move or soft-delete, both undoable from History).
/// </summary>
public sealed class DuplicateFinderForm : Form
{
    private readonly ListBox _folderList = new() { IntegralHeight = false };
    private readonly Button _addFolderButton = new() { Text = "Add..." };
    private readonly Button _removeFolderButton = new() { Text = "Remove" };
    private readonly CheckBox _recursiveCheck = new() { Text = "Include subfolders", AutoSize = true, Checked = true };

    private readonly RadioButton _modeSize = new() { Text = "Size only" };
    private readonly RadioButton _modeQuick = new() { Text = "Quick hash (recommended)", Checked = true };
    private readonly RadioButton _modeFull = new() { Text = "Full hash" };
    private readonly RadioButton _modeNameSize = new() { Text = "Name + size" };

    private readonly NumericUpDown _minSizeValue = new() { Maximum = 1_000_000, Minimum = 0, Value = 0 };
    private readonly ComboBox _minSizeUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _mediaOnlyCheck = new() { Text = "Pictures and videos only", AutoSize = true, Checked = true };
    private readonly TextBox _customExtBox = new();

    private readonly ComboBox _keepPolicyCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RadioButton _resReport = new() { Text = "Report only — no files touched", AutoSize = true, Checked = true };
    private readonly RadioButton _resMove = new() { Text = "Move duplicates to a folder:", AutoSize = true };
    private readonly RadioButton _resDelete = new() { Text = "Delete duplicates (soft delete — undo via History)", AutoSize = true };
    private readonly TextBox _moveFolderBox = new();
    private readonly Button _moveBrowseButton = new() { Text = "Browse" };

    private readonly Button _scanButton = new() { Text = "Scan" };
    private readonly Button _cancelButton = new() { Text = "Cancel", Enabled = false };
    private readonly Button _resolveButton = new() { Text = "Resolve...", AutoSize = true, Enabled = false };
    private readonly ProgressBar _progressBar = new();
    private readonly Label _statusLabel = new() { Text = "Add folders and press Scan.", AutoEllipsis = true };
    private readonly Label _summaryLabel = new()
    {
        Text = "", AutoSize = false, Height = 28, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
    };
    private readonly ListView _resultsList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowItemToolTips = true,
    };
    private readonly ToolStripMenuItem _menuOpen = new("Open file");
    private readonly ToolStripMenuItem _menuReveal = new("Reveal in Explorer");
    private readonly ToolStripMenuItem _menuExclude = new("Exclude from action");

    private CancellationTokenSource? _cts;
    private DuplicateScanResult? _scan;
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private Font? _keeperFont;
    private bool _busy;

    public DuplicateFinderForm(string? initialFolder = null)
    {
        Text = "Duplicate Finder";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1140, 720);
        MinimumSize = new Size(1000, 580);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(4) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // ===== left: options column (scrolls when the window is short) =====
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
        const AnchorStyles Wide = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        const int GroupWidth = 352;

        // --- folders ---
        var foldersGroup = new GroupBox { Text = "Folders to scan", Width = GroupWidth, Height = 178, Anchor = Wide };
        _folderList.SetBounds(10, 22, GroupWidth - 20, 84);
        _folderList.Anchor = Wide;
        _addFolderButton.SetBounds(10, 112, 80, 26);
        _removeFolderButton.SetBounds(96, 112, 80, 26);
        _recursiveCheck.Location = new Point(10, 146);
        foldersGroup.Controls.AddRange([_folderList, _addFolderButton, _removeFolderButton, _recursiveCheck]);

        // --- compare mode ---
        var compareGroup = new GroupBox { Text = "How files are compared", Width = GroupWidth, Height = 188, Anchor = Wide };
        AddRadioWithHint(compareGroup, _modeSize, "Fastest. Different files can share a size — treat matches as suspects.", 22);
        AddRadioWithHint(compareGroup, _modeQuick, "Checks size, then hashes the first and last 64 KB. Fast and reliable for media.", 62);
        AddRadioWithHint(compareGroup, _modeFull, "Reads every byte (SHA-256). Slowest, but exact.", 102);
        AddRadioWithHint(compareGroup, _modeNameSize, "Same file name and same size. Good for finding copied folder trees.", 142);

        // --- filters ---
        var filtersGroup = new GroupBox { Text = "Filters", Width = GroupWidth, Height = 136, Anchor = Wide };
        filtersGroup.Controls.Add(new Label { Text = "Ignore files smaller than", AutoSize = true, Location = new Point(10, 26) });
        _minSizeValue.SetBounds(160, 22, 80, 23);
        _minSizeUnit.SetBounds(246, 22, 64, 23);
        _minSizeUnit.Items.AddRange(["B", "KB", "MB", "GB"]);
        _minSizeUnit.SelectedIndex = 2;
        _mediaOnlyCheck.Location = new Point(10, 54);
        filtersGroup.Controls.Add(new Label { Text = "Custom extensions:", AutoSize = true, Location = new Point(10, 84) });
        _customExtBox.SetBounds(130, 80, GroupWidth - 142, 23);
        _customExtBox.Anchor = Wide;
        filtersGroup.Controls.Add(new Label
        {
            Text = "e.g. jpg png mp4 — non-empty overrides the media-only filter",
            ForeColor = SystemColors.GrayText, Location = new Point(10, 108),
            Size = new Size(GroupWidth - 20, 16), AutoEllipsis = true, Anchor = Wide,
        });
        filtersGroup.Controls.AddRange([_minSizeValue, _minSizeUnit, _mediaOnlyCheck, _customExtBox]);

        // --- resolution ---
        var resolutionGroup = new GroupBox { Text = "What to do with duplicates", Width = GroupWidth, Height = 168, Anchor = Wide };
        resolutionGroup.Controls.Add(new Label { Text = "Keep policy:", AutoSize = true, Location = new Point(10, 26) });
        _keepPolicyCombo.SetBounds(100, 22, GroupWidth - 112, 23);
        _keepPolicyCombo.Anchor = Wide;
        _keepPolicyCombo.Items.AddRange(["Oldest file", "Newest file", "Shortest path", "Longest path", "First alphabetical"]);
        _keepPolicyCombo.SelectedIndex = 0; // matches DuplicateKeepPolicy enum order
        _resReport.Location = new Point(10, 56);
        _resMove.Location = new Point(10, 80);
        _moveFolderBox.SetBounds(28, 102, GroupWidth - 116, 23);
        _moveFolderBox.Anchor = Wide;
        _moveBrowseButton.SetBounds(GroupWidth - 82, 101, 72, 25);
        _moveBrowseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _resDelete.Location = new Point(10, 132);
        resolutionGroup.Controls.AddRange([_keepPolicyCombo, _resReport, _resMove, _moveFolderBox, _moveBrowseButton, _resDelete]);

        // --- scan panel ---
        var scanPanel = new Panel { Width = GroupWidth, Height = 100, Anchor = Wide };
        _scanButton.SetBounds(10, 6, 110, 30);
        _cancelButton.SetBounds(126, 6, 80, 30);
        _progressBar.SetBounds(10, 44, GroupWidth - 20, 14);
        _progressBar.Anchor = Wide;
        _statusLabel.SetBounds(10, 62, GroupWidth - 20, 32);
        _statusLabel.Anchor = Wide;
        scanPanel.Controls.AddRange([_scanButton, _cancelButton, _progressBar, _statusLabel]);

        foreach (Control group in (Control[])[foldersGroup, compareGroup, filtersGroup, resolutionGroup, scanPanel])
        {
            int row = options.RowCount++;
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            options.Controls.Add(group, 0, row);
        }
        root.Controls.Add(options, 0, 0);

        // ===== right: results =====
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _resultsList.Columns.Add("File", 520);
        _resultsList.Columns.Add("Status", 90);
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_menuOpen, _menuReveal, new ToolStripSeparator(), _menuExclude]);
        menu.Opening += OnResultsMenuOpening;
        _resultsList.ContextMenuStrip = menu;
        right.Controls.Add(_resultsList, 0, 0);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_summaryLabel, 0, 0);
        bottom.Controls.Add(_resolveButton, 1, 0);
        right.Controls.Add(bottom, 0, 1);
        root.Controls.Add(right, 1, 0);

        Controls.Add(root);

        _addFolderButton.Click += OnAddFolder;
        _removeFolderButton.Click += (_, _) => { if (_folderList.SelectedIndex >= 0) _folderList.Items.RemoveAt(_folderList.SelectedIndex); };
        _moveBrowseButton.Click += OnBrowseMoveFolder;
        _scanButton.Click += OnScan;
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        _resolveButton.Click += OnResolve;
        _keepPolicyCombo.SelectedIndexChanged += (_, _) => RenderResults();
        _menuOpen.Click += (_, _) => { if (SelectedPath() is { } p) ExplorerShell.OpenFile(p); };
        _menuReveal.Click += (_, _) => { if (SelectedPath() is { } p) ExplorerShell.RevealFile(p); };
        _menuExclude.Click += OnToggleExclude;
        FormClosing += (_, _) => _cts?.Cancel();

        if (!string.IsNullOrWhiteSpace(initialFolder)) _folderList.Items.Add(initialFolder.Trim());
        ThemeService.FixGaps(this);
    }

    private static void AddRadioWithHint(GroupBox box, RadioButton radio, string hint, int y)
    {
        radio.AutoSize = true;
        radio.Location = new Point(10, y);
        box.Controls.Add(radio);
        box.Controls.Add(new Label
        {
            Text = hint, ForeColor = SystemColors.GrayText, Location = new Point(28, y + 18),
            Size = new Size(box.Width - 40, 16), AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        });
    }

    // ----- options -----

    private DuplicateFinderOptions? BuildOptions(out string? error)
    {
        var folders = _folderList.Items.Cast<object>().Select(o => o.ToString()!.Trim()).Where(f => f.Length > 0).ToList();
        if (folders.Count == 0) { error = "Add at least one folder to scan."; return null; }
        var missing = folders.FirstOrDefault(f => !Directory.Exists(f));
        if (missing is not null) { error = $"Folder does not exist:\n{missing}"; return null; }

        long multiplier = _minSizeUnit.SelectedIndex switch
        {
            1 => 1024L,
            2 => 1024L * 1024,
            3 => 1024L * 1024 * 1024,
            _ => 1L,
        };
        var custom = _customExtBox.Text
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => (x.StartsWith('.') ? x : "." + x).ToLowerInvariant())
            .ToHashSet();

        error = null;
        return new DuplicateFinderOptions
        {
            Folders = folders,
            Recursive = _recursiveCheck.Checked,
            CompareMode = _modeSize.Checked ? DuplicateCompareMode.SizeOnly
                : _modeFull.Checked ? DuplicateCompareMode.FullHash
                : _modeNameSize.Checked ? DuplicateCompareMode.NameAndSize
                : DuplicateCompareMode.QuickHash,
            MinFileSizeBytes = (long)_minSizeValue.Value * multiplier,
            Extensions = custom,
            MediaOnly = custom.Count == 0 && _mediaOnlyCheck.Checked,
            KeepPolicy = (DuplicateKeepPolicy)Math.Max(0, _keepPolicyCombo.SelectedIndex),
            Resolution = _resMove.Checked ? DuplicateResolution.MoveToFolder
                : _resDelete.Checked ? DuplicateResolution.Delete
                : DuplicateResolution.ReportOnly,
            MoveToFolder = _moveFolderBox.Text.Trim(),
        };
    }

    private void OnAddFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Add a folder to the duplicate scan",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK && !_folderList.Items.Contains(dlg.SelectedPath))
            _folderList.Items.Add(dlg.SelectedPath);
    }

    private void OnBrowseMoveFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Folder to move duplicates into",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _moveFolderBox.Text = dlg.SelectedPath;
            _resMove.Checked = true;
        }
    }

    // ----- scan -----

    private async void OnScan(object? sender, EventArgs e)
    {
        if (_busy) return;
        var options = BuildOptions(out var error);
        if (options is null)
        {
            MessageBox.Show(this, error, "Duplicate Finder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _scan = null;
        _excluded.Clear();
        RenderResults();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            IDuplicateFinder finder = new DuplicateFinder();
            var progress = new UiProgress(this, ApplyProgress);
            var scan = await Task.Run(() => finder.ScanAsync(options, progress, _cts.Token), _cts.Token);
            _scan = scan;
            RenderResults();
            _statusLabel.Text = $"Done — {scan.FilesScanned:N0} files scanned, {SizeFormatter.Format(scan.BytesHashed)} hashed.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Scan failed.";
            MessageBox.Show(this, ex.Message, "Duplicate Finder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ApplyProgress(SortProgress p)
    {
        if (p.TotalCount > 0)
            _progressBar.Value = Math.Clamp((int)(p.ProcessedCount * 100L / p.TotalCount), 0, 100);
        var total = p.TotalCount > 0 ? p.TotalCount.ToString("N0") : "?";
        _statusLabel.Text = $"{p.ProcessedCount:N0}/{total} — {Path.GetFileName(p.CurrentFile)}";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _scanButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _resolveButton.Enabled = !busy && _scan is { Groups.Count: > 0 };
        if (!busy) _progressBar.Value = 0;
    }

    // ----- results -----

    private void RenderResults()
    {
        _resultsList.BeginUpdate();
        _resultsList.Items.Clear();
        _resultsList.Groups.Clear();
        if (_scan is null)
        {
            _resultsList.EndUpdate();
            _summaryLabel.Text = "";
            _resolveButton.Enabled = false;
            return;
        }

        var policy = (DuplicateKeepPolicy)Math.Max(0, _keepPolicyCombo.SelectedIndex);
        foreach (var g in _scan.Groups.OrderByDescending(x => x.WastedBytes))
        {
            var lvg = new ListViewGroup(
                $"{g.Files.Count} copies · {SizeFormatter.Format(g.FileSizeBytes)} each · wasting {SizeFormatter.Format(g.WastedBytes)}");
            _resultsList.Groups.Add(lvg);
            // The engine's picker is the single source of truth: the "Keep" marker must
            // show exactly the file ResolveAsync will keep.
            var keeper = g.Files.Count > 0 ? DuplicateFinder.PickKeeper(g.Files, policy) : "";
            foreach (var f in g.Files)
            {
                bool isKeeper = string.Equals(f, keeper, StringComparison.OrdinalIgnoreCase);
                bool excluded = _excluded.Contains(f);
                var item = new ListViewItem(f, lvg) { Tag = f };
                item.SubItems.Add(isKeeper ? "Keep" : excluded ? "Excluded" : "");
                if (isKeeper)
                    item.Font = _keeperFont ??= new Font(_resultsList.Font, FontStyle.Bold);
                else if (excluded)
                    item.ForeColor = SystemColors.GrayText;
                _resultsList.Items.Add(item);
            }
        }
        _resultsList.EndUpdate();

        int redundant = _scan.Groups.Sum(x => Math.Max(0, x.Files.Count - 1));
        _summaryLabel.Text =
            $"{_scan.Groups.Count:N0} groups · {redundant:N0} redundant files · {SizeFormatter.Format(_scan.TotalWastedBytes)} reclaimable";
        _resolveButton.Enabled = !_busy && _scan.Groups.Count > 0;
    }

    private string? SelectedPath() =>
        _resultsList.SelectedItems.Count > 0 ? _resultsList.SelectedItems[0].Tag as string : null;

    private void OnResultsMenuOpening(object? sender, CancelEventArgs e)
    {
        var path = SelectedPath();
        if (path is null) { e.Cancel = true; return; }
        _menuExclude.Text = _excluded.Contains(path) ? "Include in action" : "Exclude from action";
    }

    private void OnToggleExclude(object? sender, EventArgs e)
    {
        if (SelectedPath() is not { } path) return;
        if (!_excluded.Remove(path)) _excluded.Add(path);
        RenderResults();
    }

    // ----- resolve -----

    private async void OnResolve(object? sender, EventArgs e)
    {
        if (_busy || _scan is null || _scan.Groups.Count == 0) return;
        var options = BuildOptions(out var error);
        if (options is null)
        {
            MessageBox.Show(this, error, "Duplicate Finder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (options.Resolution == DuplicateResolution.ReportOnly)
        {
            MessageBox.Show(this, "Resolution is set to \"Report only\". Choose move or delete to act on duplicates.",
                "Duplicate Finder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (options.Resolution == DuplicateResolution.MoveToFolder && options.MoveToFolder.Length == 0)
        {
            MessageBox.Show(this, "Pick a folder to move duplicates into.", "Duplicate Finder",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Honor per-file exclusions: groups shrink; groups left with one file drop out entirely.
        var filtered = new DuplicateScanResult
        {
            FilesScanned = _scan.FilesScanned,
            BytesHashed = _scan.BytesHashed,
            Elapsed = _scan.Elapsed,
        };
        foreach (var g in _scan.Groups)
        {
            var files = g.Files.Where(f => !_excluded.Contains(f)).ToList();
            if (files.Count < 2) continue;
            filtered.Groups.Add(new DuplicateGroup { Key = g.Key, FileSizeBytes = g.FileSizeBytes, Files = files });
        }
        int redundant = filtered.Groups.Sum(g => g.Files.Count - 1);
        if (redundant == 0)
        {
            MessageBox.Show(this, "Nothing to resolve — every redundant copy is excluded.", "Duplicate Finder",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        long bytes = filtered.Groups.Sum(g => g.WastedBytes);
        string prompt = options.Resolution == DuplicateResolution.Delete
            ? $"Delete {redundant:N0} redundant files ({SizeFormatter.Format(bytes)})?\n\n" +
              "Deletes are soft: the files are moved into Photon's journal backup and can be restored any time from History."
            : $"Move {redundant:N0} redundant files ({SizeFormatter.Format(bytes)}) to:\n{options.MoveToFolder}\n\n" +
              "The move is journaled and can be undone from History.";
        if (MessageBox.Show(this, prompt, "Resolve duplicates", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            IDuplicateFinder finder = new DuplicateFinder();
            var progress = new UiProgress(this, ApplyProgress);
            var result = await Task.Run(() => finder.ResolveAsync(filtered, options, progress, _cts.Token), _cts.Token);

            var summary = $"Processed: {result.Processed:N0}";
            if (result.Moved > 0) summary += $"\nMoved: {result.Moved:N0}";
            if (result.Errors.Count > 0)
            {
                summary += $"\nErrors: {result.Errors.Count:N0}";
                foreach (var (file, err) in result.Errors.Take(5))
                    summary += $"\n  {Path.GetFileName(file)}: {err}";
                if (result.Errors.Count > 5) summary += "\n  …";
            }
            summary += result.Cancelled
                ? "\n\nCancelled part-way — completed work is journaled and undoable from History."
                : "\n\nUndo any time from History.";
            MessageBox.Show(this, summary, "Resolve duplicates", MessageBoxButtons.OK,
                result.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            _scan = null;
            _excluded.Clear();
            RenderResults();
            _statusLabel.Text = "Resolution finished — run a new scan to verify.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Resolution cancelled — completed work is journaled and undoable from History.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Resolution failed.";
            MessageBox.Show(this, ex.Message, "Duplicate Finder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
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
