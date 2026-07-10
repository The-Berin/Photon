using System.Diagnostics;
using System.Management;
using Photon.App.Services;
using Photon.Core.Models;
using Photon.Core.Util;

namespace Photon.App.Forms;

/// <summary>
/// Feature 10: drive inspector — every ready volume with capacity numbers, physical-disk
/// vitals from the Windows Storage WMI namespace, and a real sequential speed test.
/// Volume→physical mapping is deliberately not attempted: the two lists stay honest.
/// </summary>
public sealed class DriveInspectorForm : Form
{
    private const int ChunkBytes = 4 * 1024 * 1024;
    private const int ChunkCount = 64; // 64 × 4 MiB = 256 MiB test file

    private readonly ListView _volumeList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, ShowItemToolTips = true,
    };
    private readonly ListView _diskList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, ShowItemToolTips = true,
    };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _speedTestButton = new() { Text = "Speed test", Enabled = false };
    private readonly Button _cancelTestButton = new() { Text = "Cancel", Enabled = false };
    private readonly ProgressBar _progressBar = new();
    private readonly Label _detailsLabel = new() { Text = "Select a volume." };
    private readonly Label _testStatusLabel = new() { Text = "", AutoEllipsis = true };
    private readonly Label _writeLabel = new() { Text = "Write: —", AutoSize = true };
    private readonly Label _readLabel = new() { Text = "Read: —", AutoSize = true };

    private CancellationTokenSource? _testCts;
    private bool _testing;

    public DriveInspectorForm()
    {
        Text = "Drive Inspector";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1060, 620);
        MinimumSize = new Size(920, 540);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(6) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        // ----- left: volumes + physical disks -----
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        toolbar.Controls.Add(_refreshButton);
        left.Controls.Add(toolbar, 0, 0);

        var volumeGroup = new GroupBox { Text = "Volumes", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 6) };
        _volumeList.Columns.Add("Volume", 70);
        _volumeList.Columns.Add("Label", 150);
        _volumeList.Columns.Add("File system", 80);
        _volumeList.Columns.Add("Total", 90, HorizontalAlignment.Right);
        _volumeList.Columns.Add("Free", 90, HorizontalAlignment.Right);
        _volumeList.Columns.Add("Used %", 70, HorizontalAlignment.Right);
        volumeGroup.Controls.Add(_volumeList);
        left.Controls.Add(volumeGroup, 0, 1);

        var diskGroup = new GroupBox { Text = "Physical disks", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 6) };
        _diskList.Columns.Add("Model", 240);
        _diskList.Columns.Add("Media", 70);
        _diskList.Columns.Add("Bus", 70);
        _diskList.Columns.Add("Health", 90);
        _diskList.Columns.Add("Size", 90, HorizontalAlignment.Right);
        diskGroup.Controls.Add(_diskList);
        left.Controls.Add(diskGroup, 0, 2);
        root.Controls.Add(left, 0, 0);

        // ----- right: selected volume + speed test -----
        const AnchorStyles Wide = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var detailsGroup = new GroupBox { Text = "Selected volume", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _detailsLabel.SetBounds(10, 24, 360, 120);
        _detailsLabel.Anchor = Wide;
        _speedTestButton.SetBounds(10, 152, 110, 30);
        _cancelTestButton.SetBounds(126, 152, 80, 30);
        _progressBar.SetBounds(10, 192, 360, 15);
        _progressBar.Anchor = Wide;
        _testStatusLabel.SetBounds(10, 212, 360, 18);
        _testStatusLabel.Anchor = Wide;
        _writeLabel.Location = new Point(10, 244);
        _writeLabel.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
        _readLabel.Location = new Point(10, 282);
        _readLabel.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
        var cacheNote = new Label
        {
            Text = "Read speed may include OS cache effects.",
            ForeColor = SystemColors.GrayText, Location = new Point(10, 318), AutoSize = true,
        };
        var testNote = new Label
        {
            Text = "The test writes a 256 MiB temp file to the volume (write-through, 4 MiB chunks), reads it back, then deletes it.",
            ForeColor = SystemColors.GrayText, Location = new Point(10, 340), Size = new Size(360, 45), Anchor = Wide,
        };
        detailsGroup.Controls.AddRange([_detailsLabel, _speedTestButton, _cancelTestButton, _progressBar,
            _testStatusLabel, _writeLabel, _readLabel, cacheNote, testNote]);
        root.Controls.Add(detailsGroup, 1, 0);

        Controls.Add(root);

        _refreshButton.Click += async (_, _) => await RefreshAllAsync();
        _volumeList.SelectedIndexChanged += (_, _) => UpdateDetails();
        _speedTestButton.Click += OnSpeedTest;
        _cancelTestButton.Click += (_, _) => _testCts?.Cancel();
        Load += async (_, _) => await RefreshAllAsync();
        FormClosing += (_, _) => _testCts?.Cancel();
        ThemeService.FixGaps(this);
    }

    // ----- volume / disk enumeration -----

    private async Task RefreshAllAsync()
    {
        if (_testing) return;
        _refreshButton.Enabled = false;
        try
        {
            var volumes = await Task.Run(EnumerateVolumes);
            _volumeList.BeginUpdate();
            _volumeList.Items.Clear();
            foreach (var v in volumes)
            {
                var item = new ListViewItem(v.Name) { Tag = v };
                item.SubItems.Add(v.VolumeLabel);
                item.SubItems.Add(v.FileSystem);
                item.SubItems.Add(SizeFormatter.Format(v.TotalBytes));
                item.SubItems.Add(SizeFormatter.Format(v.FreeBytes));
                item.SubItems.Add(v.TotalBytes > 0 ? $"{v.UsedBytes * 100d / v.TotalBytes:N0} %" : "—");
                _volumeList.Items.Add(item);
            }
            _volumeList.EndUpdate();
            UpdateDetails();

            var disks = await Task.Run(QueryPhysicalDisks);
            _diskList.BeginUpdate();
            _diskList.Items.Clear();
            foreach (var (model, media, bus, health, size) in disks)
            {
                var item = new ListViewItem(model);
                item.SubItems.Add(media);
                item.SubItems.Add(bus);
                item.SubItems.Add(health);
                item.SubItems.Add(size);
                _diskList.Items.Add(item);
            }
            _diskList.EndUpdate();
        }
        finally
        {
            _refreshButton.Enabled = true;
        }
    }

    private static List<DriveReport> EnumerateVolumes()
    {
        var list = new List<DriveReport>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return list; }
        foreach (var d in drives)
        {
            try
            {
                if (!d.IsReady) continue;
                list.Add(new DriveReport
                {
                    Name = d.Name,
                    VolumeLabel = d.VolumeLabel,
                    FileSystem = d.DriveFormat,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.AvailableFreeSpace,
                });
            }
            catch { /* volume vanished mid-enumeration (e.g. USB unplug) — skip it */ }
        }
        return list;
    }

    // Physical disks are listed separately because volume→physical-disk mapping via WMI is
    // unreliable across RAID/storage-spaces/USB bridges; showing both lists unmapped is honest.
    private static List<(string Model, string Media, string Bus, string Health, string Size)> QueryPhysicalDisks()
    {
        var rows = new List<(string, string, string, string, string)>();
        if (!OperatingSystem.IsWindows())
        {
            rows.Add(("Health data unavailable (requires Windows Storage WMI)", "—", "—", "—", "—"));
            return rows;
        }
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\microsoft\windows\storage",
                "SELECT FriendlyName, MediaType, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk");
            using var results = searcher.Get();
            foreach (var disk in results)
            {
                string model = disk["FriendlyName"]?.ToString() ?? "Unknown disk";
                string media = ToInt(disk["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unspecified" };
                string bus = ToInt(disk["BusType"]) switch { 7 => "USB", 8 => "RAID", 11 => "SATA", 17 => "NVMe", _ => "Other" };
                string health = ToInt(disk["HealthStatus"]) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" };
                string size = disk["Size"] is { } s ? SizeFormatter.Format((long)Convert.ToUInt64(s)) : "—";
                rows.Add((model, media, bus, health, size));
            }
            if (rows.Count == 0)
                rows.Add(("Health data unavailable (WMI returned no disks)", "—", "—", "—", "—"));
        }
        catch
        {
            // WMI can throw for many machine-specific reasons; degrade, never crash.
            rows.Clear();
            rows.Add(("Health data unavailable", "—", "—", "—", "—"));
        }
        return rows;
    }

    private static int ToInt(object? value)
    {
        try { return value is null ? -1 : Convert.ToInt32(value); }
        catch { return -1; }
    }

    // ----- details + speed test -----

    private DriveReport? SelectedVolume =>
        _volumeList.SelectedItems.Count > 0 ? _volumeList.SelectedItems[0].Tag as DriveReport : null;

    private void UpdateDetails()
    {
        var r = SelectedVolume;
        _speedTestButton.Enabled = r is not null && !_testing;
        if (r is null)
        {
            _detailsLabel.Text = "Select a volume.";
            return;
        }
        var bench = r.SequentialWriteMBps is null
            ? "No speed test run yet."
            : $"Write {r.SequentialWriteMBps:N1} MB/s · Read {r.SequentialReadMBps:N1} MB/s ({r.BenchmarkNote})";
        _detailsLabel.Text = string.Join(Environment.NewLine,
        [
            $"{r.Name}  {(string.IsNullOrEmpty(r.VolumeLabel) ? "(no label)" : "\"" + r.VolumeLabel + "\"")}",
            $"File system: {r.FileSystem}",
            $"Total: {SizeFormatter.Format(r.TotalBytes)}",
            $"Free: {SizeFormatter.Format(r.FreeBytes)}",
            r.TotalBytes > 0 ? $"Used: {SizeFormatter.Format(r.UsedBytes)} ({r.UsedBytes * 100d / r.TotalBytes:N0} %)" : "Used: —",
            bench,
        ]);
    }

    private async void OnSpeedTest(object? sender, EventArgs e)
    {
        if (_testing) return;
        var report = SelectedVolume;
        if (report is null) return;
        if (report.FreeBytes < 512L * 1024 * 1024)
        {
            MessageBox.Show(this, "Not enough free space on the volume for the 256 MiB test file.",
                "Speed test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _testing = true;
        _speedTestButton.Enabled = false;
        _cancelTestButton.Enabled = true;
        _refreshButton.Enabled = false;
        _writeLabel.Text = "Write: …";
        _readLabel.Text = "Read: …";
        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;
        try
        {
            var (write, read) = await Task.Run(() => RunSpeedTest(report.Name, ct, ReportTestProgress), ct);
            report.SequentialWriteMBps = write;
            report.SequentialReadMBps = read;
            report.BenchmarkNote = "read may include OS cache";
            _writeLabel.Text = $"Write: {write:N1} MB/s";
            _readLabel.Text = $"Read: {read:N1} MB/s";
            _testStatusLabel.Text = "Speed test complete.";
            UpdateDetails();
        }
        catch (OperationCanceledException)
        {
            _writeLabel.Text = "Write: —";
            _readLabel.Text = "Read: —";
            _testStatusLabel.Text = "Speed test cancelled.";
        }
        catch (Exception ex)
        {
            _writeLabel.Text = "Write: —";
            _readLabel.Text = "Read: —";
            _testStatusLabel.Text = "Speed test failed.";
            MessageBox.Show(this, ex.Message, "Speed test", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _testing = false;
            _cancelTestButton.Enabled = false;
            _refreshButton.Enabled = true;
            _speedTestButton.Enabled = SelectedVolume is not null;
            _progressBar.Value = 0;
            _testCts?.Dispose();
            _testCts = null;
        }
    }

    private void ReportTestProgress(int percent, string phase)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                _progressBar.Value = Math.Clamp(percent, 0, 100);
                _testStatusLabel.Text = phase;
            });
        }
        catch (InvalidOperationException) { /* handle torn down mid-report */ }
    }

    private static (double WriteMBps, double ReadMBps) RunSpeedTest(string volumeRoot, CancellationToken ct, Action<int, string> report)
    {
        const double TotalMiB = ChunkBytes / (1024d * 1024) * ChunkCount;
        var path = PickTestFilePath(volumeRoot);
        var chunk = new byte[ChunkBytes];
        Random.Shared.NextBytes(chunk); // incompressible-ish payload defeats transparent compression

        try
        {
            var sw = Stopwatch.StartNew();
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
            {
                for (int i = 0; i < ChunkCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    fs.Write(chunk, 0, ChunkBytes);
                    report((i + 1) * 50 / ChunkCount, $"Writing… {(i + 1) * 4} / 256 MiB");
                }
                fs.Flush(true);
            }
            sw.Stop();
            double write = TotalMiB / Math.Max(0.001, sw.Elapsed.TotalSeconds);

            sw.Restart();
            long totalRead = 0;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
            {
                int n;
                while ((n = fs.Read(chunk, 0, ChunkBytes)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    totalRead += n;
                    report(50 + (int)(totalRead * 50 / ((long)ChunkBytes * ChunkCount)),
                        $"Reading… {totalRead / (1024 * 1024)} / 256 MiB");
                }
            }
            sw.Stop();
            double read = TotalMiB / Math.Max(0.001, sw.Elapsed.TotalSeconds);
            return (write, read);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort — the name is unique */ }
        }
    }

    private static string PickTestFilePath(string volumeRoot)
    {
        var name = $"photon_speedtest_{Guid.NewGuid():N}.tmp";
        var candidate = Path.Combine(volumeRoot, name);
        try
        {
            // Probe write access at the volume root (C:\ commonly denies non-elevated writes).
            using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(candidate);
            return candidate;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var temp = Path.GetTempPath();
            if (string.Equals(Path.GetPathRoot(temp), volumeRoot, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(temp, name); // same volume, so the numbers still apply
            throw new IOException(
                $"Cannot create a test file on {volumeRoot} — access denied, and the temp folder is on a different volume.", ex);
        }
    }
}
