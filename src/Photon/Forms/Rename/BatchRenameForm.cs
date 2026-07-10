using Photon.App.Services;
using Photon.Core.Models;
using Photon.Core.Services;
using Photon.Core.Util;

namespace Photon.App.Forms;

/// <summary>
/// The batch-rename control center: live-preview grid on the left, simple/advanced
/// option stacks on the right, presets on top, journaled execution at the bottom.
/// All engine work runs off the UI thread; option edits re-plan after a short debounce.
/// </summary>
public partial class BatchRenameForm : Form
{
    private enum RowKind { Unchanged, Changed, Problem, Off, Excluded }

    private sealed class RenameRow
    {
        public required string Path { get; set; }
        public string OldName => System.IO.Path.GetFileName(Path);
        public required string Folder { get; init; }
        public long SizeBytes { get; init; }
        public bool Included { get; set; } = true;
        public string NewName { get; set; } = "";
        public string Status { get; set; } = "";
        public RowKind Kind { get; set; } = RowKind.Unchanged;
    }

    private readonly IRenameEngine _engine = new RenameEngine();
    private readonly RenamePresetStore _presets = new();

    private List<RenameRow> _rows = [];
    private List<RenamePlanItem> _currentPlan = [];
    private RenameOptions _currentPlanOptions = new();
    private int _changeCount;
    private int _problemCount;

    private readonly System.Windows.Forms.Timer _debounce;
    private int _previewVersion;
    private bool _suspendPreview;

    private bool _busy;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _loadCts;
    private bool _closeRequested;

    private bool _advancedMode;
    private bool _syncingScope;

    private static readonly HashSet<string> MediaExtensions = new(
        ScanFilter.DefaultPictureExtensions.Concat(ScanFilter.DefaultVideoExtensions),
        StringComparer.OrdinalIgnoreCase);

    public BatchRenameForm(string? initialFolder = null)
    {
        Text = "Batch Rename";
        ClientSize = new Size(1500, 900);
        MinimumSize = new Size(1150, 700);
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        MinimizeBox = false;
        KeyPreview = true;

        _debounce = new System.Windows.Forms.Timer { Interval = 250 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RebuildPreviewAsync(); };

        BuildLayout();
        BuildSimplePanel();
        BuildAdvancedPanel();
        ThemeService.FixGaps(this);
        SetMode(advanced: false);

        _presets.Load();
        RefreshPresetCombo();

        if (!string.IsNullOrWhiteSpace(initialFolder))
            _txtFolder.Text = initialFolder;

        KeyDown += (_, e) =>
        {
            // Esc closes only when idle and not cancelling an in-cell edit.
            if (e.KeyCode == Keys.Escape && !_busy
                && !_grid.IsCurrentCellInEditMode && !_rulesGrid.IsCurrentCellInEditMode)
            {
                e.Handled = true;
                Close();
            }
        };
        FormClosing += OnFormClosing;
        Shown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_txtFolder.Text) && Directory.Exists(_txtFolder.Text))
                _ = LoadFilesAsync();
        };
        UpdateCounters();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce?.Dispose();
            _loadCts?.Dispose();
            _cts?.Dispose();
            _tips?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _loadCts?.Cancel();
        if (_busy)
        {
            // Let the running rename wind down cleanly, then close.
            _cts?.Cancel();
            _closeRequested = true;
            _lblStatus.Text = "Cancelling…";
            e.Cancel = true;
        }
    }

    // ---------- file loading ----------

    private async Task LoadFilesAsync()
    {
        var folder = _txtFolder.Text.Trim();
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            MessageBox.Show(this, "Pick an existing folder first.", "Batch Rename",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        var recursive = _chkSubfolders.Checked;
        var allFiles = _chkAllFiles.Checked;

        _btnLoad.Enabled = false;
        _lblStatus.Text = "Scanning…";
        var found = new Progress<int>(n => _lblStatus.Text = $"Scanning… {n:N0} files");
        try
        {
            var rows = await Task.Run(() =>
            {
                var list = new List<RenameRow>();
                int reported = 0;
                foreach (var path in EnumerateFilesSafe(folder, recursive, ct))
                {
                    if (!allFiles && !MediaExtensions.Contains(Path.GetExtension(path)))
                        continue;
                    long size = 0;
                    try { size = new FileInfo(path).Length; } catch { /* keep the row; size stays 0 */ }
                    list.Add(new RenameRow
                    {
                        Path = path,
                        Folder = Path.GetDirectoryName(path) ?? folder,
                        SizeBytes = size,
                    });
                    if (++reported % 500 == 0) ((IProgress<int>)found).Report(reported);
                }
                list.Sort((a, b) =>
                {
                    int c = string.Compare(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : string.Compare(a.OldName, b.OldName, StringComparison.OrdinalIgnoreCase);
                });
                return list;
            }, ct);

            if (ct.IsCancellationRequested || IsDisposed) return;
            _rows = rows;
            _grid.RowCount = 0;
            _grid.RowCount = _rows.Count;
            _lblStatus.Text = $"Loaded {_rows.Count:N0} file(s).";
            RequestPreview();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!IsDisposed) _btnLoad.Enabled = true;
        }
    }

    /// <summary>Walks the tree without ever throwing on inaccessible directories.</summary>
    private static IEnumerable<string> EnumerateFilesSafe(string root, bool recursive, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            string[] files = [];
            try { files = Directory.GetFiles(dir); } catch { }
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                yield return f;
            }
            if (!recursive) yield break;
            string[] subdirs = [];
            try { subdirs = Directory.GetDirectories(dir); } catch { }
            foreach (var d in subdirs) pending.Push(d);
        }
    }

    // ---------- live preview ----------

    private void RequestPreview()
    {
        if (_suspendPreview) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RebuildPreviewAsync()
    {
        if (IsDisposed) return;
        var version = ++_previewVersion;
        var options = CollectOptions();
        var files = _rows.Where(r => r.Included).Select(r => r.Path).ToList();

        List<RenamePlanItem> plan;
        string? planError = null;
        try
        {
            plan = await Task.Run(() => _engine.BuildPlan(files, options));
        }
        catch (Exception ex)
        {
            plan = [];
            planError = ex.Message;
        }
        if (version != _previewVersion || IsDisposed) return;

        var byOldPath = new Dictionary<string, RenamePlanItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan) byOldPath[item.OldPath] = item;

        _changeCount = 0;
        _problemCount = 0;
        foreach (var row in _rows)
        {
            if (!row.Included)
            {
                row.NewName = "";
                row.Status = "off";
                row.Kind = RowKind.Off;
            }
            else if (byOldPath.TryGetValue(row.Path, out var item))
            {
                row.NewName = item.NewName;
                if (item.Problem is not null)
                {
                    row.Status = item.Problem;
                    row.Kind = RowKind.Problem;
                    _problemCount++;
                }
                else if (item.Changed)
                {
                    row.Status = "ok";
                    row.Kind = RowKind.Changed;
                    _changeCount++;
                }
                else
                {
                    row.Status = "unchanged";
                    row.Kind = RowKind.Unchanged;
                }
            }
            else
            {
                row.NewName = "";
                row.Status = "excluded by mask";
                row.Kind = RowKind.Excluded;
            }
        }

        _currentPlan = plan;
        _currentPlanOptions = options;
        _grid.Invalidate();
        UpdateCounters();
        _lblStatus.Text = planError is null ? "" : $"Preview failed: {planError}";
    }

    private void UpdateCounters()
    {
        _lblCounts.Text = $"{_rows.Count:N0} files · {_changeCount:N0} will change · {_problemCount:N0} problems";
        _btnRename.Enabled = _changeCount > 0 && !_busy;
    }

    // ---------- execution ----------

    private async void OnRenameClick(object? sender, EventArgs e)
    {
        if (_busy || _rows.Count == 0) return;
        // The guard must be set BEFORE the first await: a double-click raises two Click
        // events, and two concurrent ExecuteAsync runs over the same plan corrupt each
        // other (and the shared _cts field).
        _busy = true;
        _btnRename.Enabled = false;

        // Make sure a pending debounce can't leave us executing a stale plan.
        _debounce.Stop();
        await RebuildPreviewAsync();
        if (_changeCount == 0 || IsDisposed || _closeRequested)
        {
            _busy = false;
            if (!IsDisposed)
            {
                UpdateCounters();
                if (_closeRequested) Close();
            }
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Rename {_changeCount:N0} file(s)? This is undoable via Sort > History.",
            "Batch Rename", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK)
        {
            _busy = false;
            UpdateCounters();
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        SetBusyUi(true);

        var progress = new Progress<SortProgress>(p =>
        {
            if (IsDisposed) return;
            if (p.TotalCount > 0)
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Maximum = p.TotalCount;
                _progress.Value = Math.Min(p.ProcessedCount, p.TotalCount);
            }
            _lblStatus.Text = p.CurrentFile.Length > 0
                ? $"{p.ProcessedCount:N0}/{p.TotalCount:N0} — {Path.GetFileName(p.CurrentFile)}"
                : $"{p.ProcessedCount:N0}/{p.TotalCount:N0}";
        });

        RenameResult? result = null;
        string? fatal = null;
        try
        {
            result = await _engine.ExecuteAsync(_currentPlan, _currentPlanOptions, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            fatal = "Cancelled.";
        }
        catch (Exception ex)
        {
            fatal = ex.Message;
        }
        finally
        {
            _busy = false;
            // Dispose the captured local, never the shared field: another handler may
            // have replaced _cts by the time this run winds down.
            if (ReferenceEquals(_cts, cts)) _cts = null;
            cts.Dispose();
            if (!IsDisposed) SetBusyUi(false);
        }
        if (IsDisposed) return;

        if (result is not null)
        {
            var summary = $"Renamed {result.Renamed:N0} file(s), skipped {result.Skipped:N0}." +
                          (result.Errors.Count > 0 ? $" {result.Errors.Count:N0} error(s) below." : "") +
                          "\r\nUndo any time via Sort > History.";
            using var dlg = new RenameSummaryDialog(summary, result.Errors);
            dlg.ShowDialog(this);
        }
        else if (fatal is not null)
        {
            MessageBox.Show(this, fatal, "Batch Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Reflect the new on-disk reality.
        await LoadFilesAsync();
        if (_closeRequested) Close();
    }

    private void SetBusyUi(bool busy)
    {
        _btnRename.Enabled = !busy && _changeCount > 0;
        _btnCancel.Visible = busy;
        _progress.Visible = busy;
        if (busy)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Value = 0;
        }
        else
        {
            _lblStatus.Text = "";
        }
        _topPanel.Enabled = !busy;
        _optionsColumn.Enabled = !busy;
        _grid.ReadOnly = busy;
    }

    // ---------- grid virtual mode ----------

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var row = _rows[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            ColInclude => row.Included,
            ColOld => row.OldName,
            ColNew => row.NewName,
            ColFolder => row.Folder,
            ColSize => SizeFormatter.Format(row.SizeBytes),
            ColStatus => row.Status,
            _ => null,
        };
    }

    private void OnCellValuePushed(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count || e.ColumnIndex != ColInclude) return;
        _rows[e.RowIndex].Included = e.Value is true;
        RequestPreview();
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var row = _rows[e.RowIndex];
        switch (row.Kind)
        {
            case RowKind.Changed:
                e.CellStyle!.BackColor = ThemeService.GridChangedBack;
                break;
            case RowKind.Problem:
                e.CellStyle!.BackColor = ThemeService.GridProblemBack;
                if (e.ColumnIndex == ColStatus) e.CellStyle.ForeColor = ThemeService.GridProblemText;
                break;
            case RowKind.Off:
            case RowKind.Excluded:
                e.CellStyle!.ForeColor = ThemeService.GridDimText;
                break;
        }
    }

    private void SetAllIncluded(Func<RenameRow, bool> included)
    {
        foreach (var row in _rows) row.Included = included(row);
        _grid.Invalidate();
        RequestPreview();
    }
}
