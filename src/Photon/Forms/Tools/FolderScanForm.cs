using Photon.App.Interop;
using Photon.Core.Models;
using Photon.Core.Services;
using Photon.Core.Util;

namespace Photon.App.Forms;

/// <summary>
/// Feature 2: quick drive/folder scan — total size, media breakdown, per-extension table,
/// and estimates for what a copy-sort of the folder would take.
/// </summary>
public sealed class FolderScanForm : Form
{
    private const double HddBytesPerSecond = 80d * 1024 * 1024;
    private const double SataBytesPerSecond = 350d * 1024 * 1024;
    private const double NvmeBytesPerSecond = 1200d * 1024 * 1024;

    /// <summary>Raised when the user sends the scanned folder to the main sorter.</summary>
    public event Action<string>? SourceChosen;

    private readonly TextBox _folderBox = new();
    private readonly Button _browseButton = new() { Text = "Browse...", AutoSize = true };
    private readonly Button _scanButton = new() { Text = "Scan", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Button _sendButton = new() { Text = "Send to sorter", AutoSize = true, Enabled = false };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true };
    private readonly Label _statusLabel = new()
    {
        Text = "Pick a folder or drive root, then press Scan.",
        AutoSize = false, Height = 20, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
    };
    private readonly ListView _extensionList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, ShowItemToolTips = true,
    };
    private readonly ToolTip _tips = new();

    private readonly Label _valFiles, _valFolders, _valBytes, _valPictures, _valVideos, _valOther,
        _valDepth, _valOldest, _valNewest, _valInaccessible, _valDuration;
    private readonly LinkLabel _valLargest;
    private readonly Label _valMediaBytes, _valEstHdd, _valEstSata, _valEstNvme, _valFree, _verdictLabel;

    private CancellationTokenSource? _cts;
    private string? _scannedFolder;
    private string? _largestFilePath;

    public FolderScanForm(string? initialFolder = null)
    {
        Text = "Folder Scan";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(880, 620);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // folder row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // content
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // bottom buttons

        // --- folder row ---
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, AutoSize = true };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _folderBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        top.Controls.Add(_folderBox, 1, 0);
        top.Controls.Add(_browseButton, 2, 0);
        top.Controls.Add(_scanButton, 3, 0);
        top.Controls.Add(_cancelButton, 4, 0);
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(_statusLabel, 0, 1);

        // --- content: summary + estimates on the left, extension table on the right ---
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        var summaryGroup = new GroupBox { Text = "Summary", Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        var summaryTable = NewKeyValueTable();
        summaryGroup.Controls.Add(summaryTable);
        _valFiles = AddRow(summaryTable, "Total files");
        _valFolders = AddRow(summaryTable, "Total folders");
        _valBytes = AddRow(summaryTable, "Total size");
        _valPictures = AddRow(summaryTable, "Pictures");
        _valVideos = AddRow(summaryTable, "Videos");
        _valOther = AddRow(summaryTable, "Other files");
        _valDepth = AddRow(summaryTable, "Max folder depth");
        _valLargest = new LinkLabel { Text = "—", AutoSize = true, Enabled = false, Margin = RowValueMargin };
        AddRowControl(summaryTable, "Largest file", _valLargest);
        _valOldest = AddRow(summaryTable, "Oldest file");
        _valNewest = AddRow(summaryTable, "Newest file");
        _valInaccessible = AddRow(summaryTable, "Inaccessible items");
        _valDuration = AddRow(summaryTable, "Scan duration");
        left.Controls.Add(summaryGroup, 0, 0);

        var estGroup = new GroupBox { Text = "Estimates", Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };
        var estTable = NewKeyValueTable();
        estGroup.Controls.Add(estTable);
        _valMediaBytes = AddRow(estTable, "Media to copy-sort");
        _valEstHdd = AddRow(estTable, "Est. time, HDD ~80 MB/s");
        _valEstSata = AddRow(estTable, "Est. time, SATA SSD ~350 MB/s");
        _valEstNvme = AddRow(estTable, "Est. time, NVMe ~1200 MB/s");
        _valFree = AddRow(estTable, "Free space on volume");
        _verdictLabel = new Label { Text = "", AutoSize = true, Margin = new Padding(3, 8, 3, 2) };
        _verdictLabel.Font = new Font(Font, FontStyle.Bold);
        int vrow = estTable.RowCount++;
        estTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        estTable.Controls.Add(_verdictLabel, 0, vrow);
        estTable.SetColumnSpan(_verdictLabel, 2);
        left.Controls.Add(estGroup, 0, 1);
        content.Controls.Add(left, 0, 0);

        var extGroup = new GroupBox { Text = "By extension", Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8) };
        _extensionList.Columns.Add("Extension", 90);
        _extensionList.Columns.Add("Files", 80, HorizontalAlignment.Right);
        _extensionList.Columns.Add("Size", 100, HorizontalAlignment.Right);
        _extensionList.Columns.Add("% of size", 80, HorizontalAlignment.Right);
        extGroup.Controls.Add(_extensionList);
        content.Controls.Add(extGroup, 1, 0);
        root.Controls.Add(content, 0, 2);

        // --- bottom buttons ---
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        bottom.Controls.Add(_closeButton);
        bottom.Controls.Add(_sendButton);
        root.Controls.Add(bottom, 0, 3);

        Controls.Add(root);

        _browseButton.Click += OnBrowse;
        _scanButton.Click += OnScan;
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        _sendButton.Click += OnSendToSorter;
        _closeButton.Click += (_, _) => Close();
        _valLargest.LinkClicked += (_, _) => { if (_largestFilePath is not null) ExplorerShell.RevealFile(_largestFilePath); };
        FormClosing += (_, _) => _cts?.Cancel();

        if (!string.IsNullOrWhiteSpace(initialFolder)) _folderBox.Text = initialFolder.Trim();
    }

    // ----- layout helpers -----

    private static readonly Padding RowCaptionMargin = new(3, 5, 12, 2);
    private static readonly Padding RowValueMargin = new(3, 5, 3, 2);

    private static TableLayoutPanel NewKeyValueTable()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static Label AddRow(TableLayoutPanel table, string caption)
    {
        var value = new Label { Text = "—", AutoSize = true, Margin = RowValueMargin };
        AddRowControl(table, caption, value);
        return value;
    }

    private static void AddRowControl(TableLayoutPanel table, string caption, Control value)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = RowCaptionMargin }, 0, row);
        table.Controls.Add(value, 1, row);
    }

    // ----- behavior -----

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Choose the folder or drive to scan",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        var current = _folderBox.Text.Trim();
        if (current.Length > 0 && Directory.Exists(current)) dlg.SelectedPath = current;
        if (dlg.ShowDialog(this) == DialogResult.OK) _folderBox.Text = dlg.SelectedPath;
    }

    private async void OnScan(object? sender, EventArgs e)
    {
        var folder = _folderBox.Text.Trim();
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            MessageBox.Show(this, "Pick an existing folder or drive root first.", "Folder Scan",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _cts = new CancellationTokenSource();
        SetScanning(true);
        var progress = new UiProgress(this, p =>
            _statusLabel.Text = $"Scanning… {p.ProcessedCount:N0} files seen — {p.CurrentFile}");
        try
        {
            IFolderScanner scanner = new FolderScanner();
            var report = await Task.Run(() => scanner.ScanAsync(folder, progress, _cts.Token), _cts.Token);
            _scannedFolder = folder;
            ShowReport(report, folder);
            _sendButton.Enabled = true;
            _statusLabel.Text = $"Scan complete — {report.TotalFiles:N0} files, {SizeFormatter.Format(report.TotalBytes)} in {FormatDuration(report.ScanDuration)}.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Scan failed.";
            MessageBox.Show(this, ex.Message, "Folder Scan", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetScanning(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void SetScanning(bool scanning)
    {
        _scanButton.Enabled = !scanning;
        _browseButton.Enabled = !scanning;
        _folderBox.Enabled = !scanning;
        _cancelButton.Enabled = scanning;
        if (scanning) _sendButton.Enabled = false;
    }

    private void ShowReport(FolderScanReport report, string folder)
    {
        _valFiles.Text = report.TotalFiles.ToString("N0");
        _valFolders.Text = report.TotalFolders.ToString("N0");
        _valBytes.Text = SizeFormatter.Format(report.TotalBytes);
        _valPictures.Text = $"{report.PictureCount:N0} · {SizeFormatter.Format(report.PictureBytes)}";
        _valVideos.Text = $"{report.VideoCount:N0} · {SizeFormatter.Format(report.VideoBytes)}";
        _valOther.Text = $"{report.OtherCount:N0} · {SizeFormatter.Format(report.OtherBytes)}";
        _valDepth.Text = report.MaxDepth.ToString("N0");
        if (report.LargestFilePath is { } largest)
        {
            _largestFilePath = largest;
            _valLargest.Text = $"{Path.GetFileName(largest)} ({SizeFormatter.Format(report.LargestFileBytes)})";
            _valLargest.Enabled = true;
            _tips.SetToolTip(_valLargest, largest + Environment.NewLine + "Click to reveal in Explorer");
        }
        else
        {
            _largestFilePath = null;
            _valLargest.Text = "—";
            _valLargest.Enabled = false;
        }
        _valOldest.Text = report.OldestFileDate?.ToString("g") ?? "—";
        _valNewest.Text = report.NewestFileDate?.ToString("g") ?? "—";
        _valInaccessible.Text = report.InaccessibleItems.ToString("N0");
        _valDuration.Text = FormatDuration(report.ScanDuration);

        _extensionList.BeginUpdate();
        _extensionList.Items.Clear();
        foreach (var (ext, data) in report.ByExtension.OrderByDescending(kv => kv.Value.Bytes))
        {
            var item = new ListViewItem(ext.Length == 0 ? "(none)" : ext);
            item.SubItems.Add(data.Count.ToString("N0"));
            item.SubItems.Add(SizeFormatter.Format(data.Bytes));
            item.SubItems.Add(report.TotalBytes > 0 ? $"{data.Bytes * 100d / report.TotalBytes:N1} %" : "—");
            _extensionList.Items.Add(item);
        }
        _extensionList.EndUpdate();

        long mediaBytes = report.PictureBytes + report.VideoBytes;
        _valMediaBytes.Text = $"{SizeFormatter.Format(mediaBytes)} (pictures + videos)";
        _valEstHdd.Text = FormatDuration(report.EstimateSortTime(HddBytesPerSecond));
        _valEstSata.Text = FormatDuration(report.EstimateSortTime(SataBytesPerSecond));
        _valEstNvme.Text = FormatDuration(report.EstimateSortTime(NvmeBytesPerSecond));

        long free = TryGetFreeBytes(folder);
        if (free >= 0)
        {
            _valFree.Text = SizeFormatter.Format(free);
            bool fits = free >= mediaBytes + SortPlan.SafetyMarginBytes;
            _verdictLabel.Text = fits
                ? "Enough room to copy-sort on this volume."
                : "NOT enough room to copy-sort on this volume.";
            _verdictLabel.ForeColor = fits ? Color.Green : Color.Firebrick;
        }
        else
        {
            _valFree.Text = "unknown";
            _verdictLabel.Text = "Free space unknown — cannot judge copy-sort headroom.";
            _verdictLabel.ForeColor = SystemColors.ControlText;
        }
    }

    private static long TryGetFreeBytes(string folder)
    {
        try
        {
            var rootPath = Path.GetPathRoot(Path.GetFullPath(folder));
            return string.IsNullOrEmpty(rootPath) ? -1 : new DriveInfo(rootPath).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalSeconds < 1 ? "under 1s"
        : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s"
        : $"{t.Seconds}s";

    private void OnSendToSorter(object? sender, EventArgs e)
    {
        var folder = _scannedFolder ?? _folderBox.Text.Trim();
        if (folder.Length == 0) return;
        SourceChosen?.Invoke(folder);
        Close();
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
