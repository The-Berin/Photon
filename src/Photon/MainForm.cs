using System.Globalization;
using System.Text;
using Photon.App.Forms;
using Photon.App.Services;
using Photon.Core.Models;
using Photon.Core.Services;
using Photon.Core.Util;

namespace Photon.App;

public partial class MainForm : Form
{
    private const int PreviewMaxThumbnails = 24;
    private const string GitHubUrl = "https://github.com/The-Berin/Photon";

    private readonly SettingsService _settingsService;
    private readonly ThumbnailService _thumbnails = new();
    private readonly KeepAwakeService _keepAwake = new();
    private readonly SoundService _sound = new();
    private readonly WhenDoneService _whenDoneService = new();

    private List<MediaFile> _scannedFiles = [];
    /// <summary>Folder _scannedFiles came from; empty while a scan is pending/in flight.</summary>
    private string _scannedFolder = "";
    private SortPlan? _currentPlan;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _planCts;
    private CancellationTokenSource? _thumbCts;
    private CancellationTokenSource? _runCts;
    private bool _running;
    private bool _suppress;
    private bool _darkRestartNoteShown;

    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _planDebounce = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer _rescanDebounce = new() { Interval = 700 };
    private readonly System.Windows.Forms.Timer _saveDebounce = new() { Interval = 800 };

    private AppSettings Settings => _settingsService.Current;
    private SortOptions Options => _settingsService.Current.Options;

    public MainForm(SettingsService settingsService)
    {
        _settingsService = settingsService;
        BuildUi();
        WireEvents();
        ApplyWindowPlacement();
        ApplySettingsToUi();
        UpdateClock();
        _clockTimer.Start();
    }

    // ----- engine construction (concrete Photon.Core.Services classes built in parallel) -----

    private static IFileScanner CreateScanner() => new FileScanner();
    private static ISortPlanner CreatePlanner() => new SortPlanner(new MetadataReader(), new DateResolver());
    private static ISortExecutor CreateExecutor() => new SortExecutor(new JournalService());

    // ----- wiring -----

    private void WireEvents()
    {
        _btnBrowseSource.Click += (_, _) => BrowseSource();
        _txtSource.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            AdoptSource(_txtSource.Text);
        };
        // A typed/pasted path must also be adopted when focus leaves the box (e.g. the
        // user clicks Sort) — otherwise the run would act on the previously scanned folder.
        _txtSource.Leave += (_, _) =>
        {
            if (_suppress || _running) return;
            var typed = _txtSource.Text.Trim().Trim('"');
            if (!string.Equals(typed, Options.SourceFolder, StringComparison.OrdinalIgnoreCase))
                AdoptSource(typed);
        };
        _chkCustomOutput.CheckedChanged += (_, e) =>
        {
            _txtOutput.Enabled = _btnBrowseOutput.Enabled = _chkCustomOutput.Checked;
            OnPlanOptionChanged(_chkCustomOutput, e);
        };
        _txtOutput.TextChanged += OnPlanOptionChanged;
        _btnBrowseOutput.Click += (_, _) => BrowseOutput();

        foreach (var rb in (RadioButton[])[_rbYearOnly, _rbYearMonth, _rbYearMonthDay, _rbMonthNumber, _rbMonthName,
                     _rbExifThenFile, _rbExifOnly, _rbFileThenExif, _rbFileOnly,
                     _rbDupRename, _rbDupSkip, _rbDupOverwrite, _rbCopy, _rbMove])
            rb.CheckedChanged += OnPlanOptionChanged;
        _chkIncludeTime.CheckedChanged += OnPlanOptionChanged;
        _txtUnknownDate.TextChanged += OnPlanOptionChanged;
        _chkGroupCamera.CheckedChanged += OnPlanOptionChanged;
        _chkDetectDuplicates.CheckedChanged += OnPlanOptionChanged;
        _chkMoveDuplicates.CheckedChanged += OnPlanOptionChanged;

        _chkPictures.CheckedChanged += OnScanOptionChanged;
        _chkVideos.CheckedChanged += OnScanOptionChanged;
        _chkRecursive.CheckedChanged += OnScanOptionChanged;
        _txtCustomExt.TextChanged += OnScanOptionChanged;
        _btnResetExt.Click += (_, _) => ResetExtensions();

        _chkWriteLog.CheckedChanged += OnCosmeticOptionChanged;
        _chkExportCsv.CheckedChanged += OnCosmeticOptionChanged;
        _txtWav.TextChanged += OnCosmeticOptionChanged;
        _btnBrowseWav.Click += (_, _) => BrowseWav();
        _chkDarkMode.CheckedChanged += (_, _) => { if (!_suppress) ApplyDarkMode(_chkDarkMode.Checked); };
        _chkKeepDisplay.CheckedChanged += OnKeepAwakeToggled;
        _chkKeepAwakeRun.CheckedChanged += OnKeepAwakeToggled;
        _cboSizeUnit.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppress) ApplySizeUnit((SizeUnit)Math.Max(0, _cboSizeUnit.SelectedIndex));
        };
        _cboWhenDone.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppress) ApplyWhenDone((WhenDoneAction)Math.Max(0, _cboWhenDone.SelectedIndex));
        };
        _cboStatusWhenDone.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppress) ApplyWhenDone((WhenDoneAction)Math.Max(0, _cboStatusWhenDone.SelectedIndex));
        };

        _btnSort.Click += async (_, _) => await RunSortAsync();
        _btnStop.Click += (_, _) => StopRun();
        _btnSeeAll.Click += (_, _) => ShowSeeAll();

        _clockTimer.Tick += (_, _) => UpdateClock();
        _planDebounce.Tick += (_, _) => { _planDebounce.Stop(); RefreshPlan(); };
        _rescanDebounce.Tick += (_, _) => { _rescanDebounce.Stop(); StartScan(); };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); _settingsService.Save(); };

        _miRecent.DropDownOpening += (_, _) => RebuildRecentMenu();
        FormClosing += OnMainFormClosing;
        FormClosed += (_, _) =>
        {
            _clockTimer.Dispose();
            _planDebounce.Dispose();
            _rescanDebounce.Dispose();
            _saveDebounce.Dispose();
            _thumbnails.Dispose();
            _sound.Dispose();
        };
    }

    // ----- settings <-> controls -----

    private void ApplySettingsToUi()
    {
        _suppress = true;
        try
        {
            var o = Options;
            _txtSource.Text = o.SourceFolder;
            _chkCustomOutput.Checked = !string.IsNullOrWhiteSpace(o.OutputFolder);
            _txtOutput.Text = o.OutputFolder ?? "";
            _txtOutput.Enabled = _btnBrowseOutput.Enabled = _chkCustomOutput.Checked;

            _rbYearOnly.Checked = o.Structure == FolderStructure.YearOnly;
            _rbYearMonth.Checked = o.Structure == FolderStructure.YearMonth;
            _rbYearMonthDay.Checked = o.Structure == FolderStructure.YearMonthDay;
            _rbMonthNumber.Checked = o.MonthFormat == MonthFormat.Number;
            _rbMonthName.Checked = o.MonthFormat == MonthFormat.Name;
            _chkIncludeTime.Checked = o.IncludeTimeSubfolder;
            _chkGroupCamera.Checked = o.GroupByCamera;
            _rbExifThenFile.Checked = o.DateSource == DateSource.ExifThenFileDate;
            _rbExifOnly.Checked = o.DateSource == DateSource.ExifOnly;
            _rbFileThenExif.Checked = o.DateSource == DateSource.FileDateThenExif;
            _rbFileOnly.Checked = o.DateSource == DateSource.FileDateOnly;
            _txtUnknownDate.Text = o.UnknownDateFolderName;

            _rbDupRename.Checked = o.DuplicateHandling == DuplicateHandling.Rename;
            _rbDupSkip.Checked = o.DuplicateHandling == DuplicateHandling.Skip;
            _rbDupOverwrite.Checked = o.DuplicateHandling == DuplicateHandling.Overwrite;
            _chkDetectDuplicates.Checked = o.DetectExactDuplicates;
            _chkMoveDuplicates.Checked = o.MoveDuplicatesToSubfolder;
            _chkPictures.Checked = o.IncludePictures;
            _chkVideos.Checked = o.IncludeVideos;
            _txtCustomExt.Text = o.CustomExtensions;
            _chkRecursive.Checked = o.IncludeSubfolders;
            _rbCopy.Checked = o.Action == SortAction.Copy;
            _rbMove.Checked = o.Action == SortAction.Move;

            _chkWriteLog.Checked = o.WriteLogFile;
            _chkExportCsv.Checked = o.ExportCsvSummary;
            _txtWav.Text = o.SoundWavPath;
            var dark = Settings.Theme == AppTheme.Dark;
            _chkDarkMode.Checked = dark;
            _miDarkMode.Checked = dark;
            _chkKeepDisplay.Checked = o.KeepAwake;
            _chkKeepAwakeRun.Checked = o.KeepAwake;
            _cboSizeUnit.SelectedIndex = (int)o.SizeUnit;
            _cboWhenDone.SelectedIndex = (int)o.WhenDone;
            _cboStatusWhenDone.SelectedIndex = (int)o.WhenDone;
            UpdateSizeUnitMenuChecks();

            TopMost = Settings.AlwaysOnTop;
            _miAlwaysOnTop.Checked = Settings.AlwaysOnTop;
            RebuildRecentMenu();
        }
        finally
        {
            _suppress = false;
        }
        StartScan();
    }

    /// <summary>One-way pull of every option control into the settings object.</summary>
    private void SyncOptionsFromControls()
    {
        var o = Options;
        // Trim quotes like the source box does: Explorer's "Copy as path" wraps in quotes,
        // which would otherwise resolve as a relative path under the app's CWD.
        var output = _txtOutput.Text.Trim().Trim('"');
        o.OutputFolder = _chkCustomOutput.Checked && output.Length > 0 ? output : null;
        o.Structure = _rbYearOnly.Checked ? FolderStructure.YearOnly
            : _rbYearMonth.Checked ? FolderStructure.YearMonth : FolderStructure.YearMonthDay;
        o.MonthFormat = _rbMonthNumber.Checked ? MonthFormat.Number : MonthFormat.Name;
        o.IncludeTimeSubfolder = _chkIncludeTime.Checked;
        o.GroupByCamera = _chkGroupCamera.Checked;
        o.DateSource = _rbExifOnly.Checked ? DateSource.ExifOnly
            : _rbFileThenExif.Checked ? DateSource.FileDateThenExif
            : _rbFileOnly.Checked ? DateSource.FileDateOnly : DateSource.ExifThenFileDate;
        o.UnknownDateFolderName = string.IsNullOrWhiteSpace(_txtUnknownDate.Text)
            ? "Unknown Date" : _txtUnknownDate.Text.Trim();
        o.DuplicateHandling = _rbDupSkip.Checked ? DuplicateHandling.Skip
            : _rbDupOverwrite.Checked ? DuplicateHandling.Overwrite : DuplicateHandling.Rename;
        o.DetectExactDuplicates = _chkDetectDuplicates.Checked;
        o.MoveDuplicatesToSubfolder = _chkMoveDuplicates.Checked;
        o.IncludePictures = _chkPictures.Checked;
        o.IncludeVideos = _chkVideos.Checked;
        o.CustomExtensions = _txtCustomExt.Text.Trim();
        o.IncludeSubfolders = _chkRecursive.Checked;
        o.Action = _rbMove.Checked ? SortAction.Move : SortAction.Copy;
        o.WriteLogFile = _chkWriteLog.Checked;
        o.ExportCsvSummary = _chkExportCsv.Checked;
        o.SoundWavPath = _txtWav.Text.Trim();
        o.KeepAwake = _chkKeepDisplay.Checked;
        o.SizeUnit = (SizeUnit)Math.Max(0, _cboSizeUnit.SelectedIndex);
        o.WhenDone = (WhenDoneAction)Math.Max(0, _cboWhenDone.SelectedIndex);
    }

    private void OnPlanOptionChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        SyncOptionsFromControls();
        ScheduleSave();
        SchedulePlan();
    }

    private void OnScanOptionChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        SyncOptionsFromControls();
        ScheduleSave();
        _rescanDebounce.Stop();
        _rescanDebounce.Start();
    }

    private void OnCosmeticOptionChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        SyncOptionsFromControls();
        ScheduleSave();
    }

    private void OnKeepAwakeToggled(object? sender, EventArgs e)
    {
        if (_suppress) return;
        var value = ((CheckBox)sender!).Checked;
        _suppress = true;
        _chkKeepDisplay.Checked = value;
        _chkKeepAwakeRun.Checked = value;
        _suppress = false;
        Options.KeepAwake = value;
        ScheduleSave();
    }

    private void ApplySizeUnit(SizeUnit unit)
    {
        _suppress = true;
        _cboSizeUnit.SelectedIndex = (int)unit;
        UpdateSizeUnitMenuChecks();
        _suppress = false;
        Options.SizeUnit = unit;
        UpdateInfoLine();
        ScheduleSave();
    }

    private void UpdateSizeUnitMenuChecks()
    {
        foreach (var item in _miSizeUnitItems)
            item.Checked = (SizeUnit)item.Tag! == (SizeUnit)Math.Max(0, _cboSizeUnit.SelectedIndex);
    }

    private void ApplyWhenDone(WhenDoneAction action)
    {
        _suppress = true;
        _cboWhenDone.SelectedIndex = (int)action;
        _cboStatusWhenDone.SelectedIndex = (int)action;
        _suppress = false;
        Options.WhenDone = action;
        ScheduleSave();
    }

    private void ApplyDarkMode(bool dark)
    {
        _suppress = true;
        _chkDarkMode.Checked = dark;
        _miDarkMode.Checked = dark;
        _suppress = false;
        Settings.Theme = dark ? AppTheme.Dark : AppTheme.Light;
        ThemeService.ApplyBestEffort(this, dark);
        ScheduleSave();
        if (!_darkRestartNoteShown)
        {
            _darkRestartNoteShown = true;
            MessageBox.Show(this,
                "This is a best-effort recolor — full dark mode applies after restarting Photon.",
                "Dark mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ScheduleSave()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void SchedulePlan()
    {
        _planDebounce.Stop();
        _planDebounce.Start();
    }

    // ----- source / scanning -----

    private void BrowseSource()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Choose the folder to sort",
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(_txtSource.Text)) dlg.InitialDirectory = _txtSource.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) AdoptSource(dlg.SelectedPath);
    }

    private void BrowseOutput()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Choose the output folder",
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(_txtOutput.Text)) dlg.InitialDirectory = _txtOutput.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtOutput.Text = dlg.SelectedPath;
    }

    private void BrowseWav()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose a WAV file",
            Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtWav.Text = dlg.FileName;
    }

    private void AdoptSource(string folder)
    {
        folder = folder.Trim().Trim('"');
        Options.SourceFolder = folder;
        if (_txtSource.Text != folder)
        {
            _suppress = true;
            _txtSource.Text = folder;
            _suppress = false;
        }
        if (Directory.Exists(folder))
        {
            _settingsService.AddRecentSource(folder);
            RebuildRecentMenu();
        }
        ScheduleSave();
        StartScan();
    }

    private async void StartScan()
    {
        if (_running) return;
        _scanCts?.Cancel();
        _planCts?.Cancel();
        _currentPlan = null;
        _scannedFolder = "";
        Text = "Photon";

        var folder = Options.SourceFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _scannedFiles = [];
            UpdateInfoLine();
            RefreshPreview();
            UpdateSortAvailability();
            SetStatus(string.IsNullOrWhiteSpace(folder) ? "Ready" : "Source folder not found");
            return;
        }

        var cts = new CancellationTokenSource();
        _scanCts = cts;
        SetStatus("Scanning...");
        try
        {
            var filter = ScanFilter.FromSortOptions(Options);
            var progress = new Progress<int>(n =>
            {
                if (!cts.IsCancellationRequested) SetStatus($"Scanning... {n:N0} file(s) found");
            });
            var files = await CreateScanner().ScanAsync(folder, filter, progress, cts.Token);
            if (cts.IsCancellationRequested) return;

            _scannedFiles = files;
            _scannedFolder = folder;
            SetStatus("Ready");
            UpdateInfoLine();
            RefreshPreview();
            UpdateSortAvailability();
            RefreshPlan();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) SetStatus("Scan failed: " + ex.Message);
        }
    }

    private async void RefreshPlan()
    {
        if (_running) return;
        _planCts?.Cancel();
        if (_scannedFiles.Count == 0)
        {
            _currentPlan = null;
            UpdateInfoLine();
            return;
        }
        var cts = new CancellationTokenSource();
        _planCts = cts;
        try
        {
            var plan = await CreatePlanner().BuildPlanAsync(_scannedFiles, Options.Clone(), null, cts.Token);
            if (cts.IsCancellationRequested) return;
            _currentPlan = plan;
            UpdateInfoLine();
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Dry-run is best-effort; the real run re-plans with full error reporting.
        }
    }

    private void UpdateSortAvailability()
    {
        var canSort = !_running && _scannedFiles.Count > 0;
        _btnSort.Enabled = canSort;
        _miStart.Enabled = canSort;
        _miPreviewSort.Enabled = canSort;
        _btnSeeAll.Enabled = _scannedFiles.Count > 0;
        _miSeeAllFiles.Enabled = _scannedFiles.Count > 0;
    }

    private void UpdateInfoLine()
    {
        var unit = Options.SizeUnit;
        var total = _scannedFiles.Sum(f => f.SizeBytes);
        var need = _currentPlan is { } p ? SizeFormatter.Format(p.RequiredBytes, unit) : "—";
        // long.MaxValue is the planner's "could not determine free space" sentinel
        // (UNC/unresolvable destination) — never show it as a real size.
        var free = _currentPlan is { } q && q.DestinationFreeBytes != long.MaxValue
            ? SizeFormatter.Format(q.DestinationFreeBytes, unit) : "—";
        _lblInfo.Text = $"{_scannedFiles.Count:N0} file(s) | {SizeFormatter.Format(total, unit)} | Need {need} | Free {free}";
    }

    // ----- preview thumbnails -----

    private async void RefreshPreview()
    {
        _thumbCts?.Cancel();
        var cts = new CancellationTokenSource();
        _thumbCts = cts;

        var old = _previewFlow.Controls.Cast<Control>().ToList();
        _previewFlow.Controls.Clear();
        foreach (var c in old)
        {
            if (c is PictureBox pb) pb.Image?.Dispose();
            c.Dispose();
        }

        _lblMoreFiles.Text = _scannedFiles.Count > PreviewMaxThumbnails
            ? $"+{_scannedFiles.Count - PreviewMaxThumbnails:N0} more files"
            : "";
        if (_scannedFiles.Count == 0)
        {
            _lblPreviewPlaceholder.Visible = true;
            _lblPreviewPlaceholder.BringToFront();
            return;
        }
        _lblPreviewPlaceholder.Visible = false;

        var boxes = new List<(PictureBox Box, string Path)>();
        foreach (var file in _scannedFiles.Take(PreviewMaxThumbnails))
        {
            var box = new PictureBox
            {
                Size = new Size(96, 96),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(3),
                BackColor = SystemColors.ControlLight,
                BorderStyle = BorderStyle.FixedSingle,
            };
            _toolTip.SetToolTip(box, file.FileName);
            _previewFlow.Controls.Add(box);
            boxes.Add((box, file.FilePath));
        }

        foreach (var (box, path) in boxes)
        {
            if (cts.IsCancellationRequested) return;
            Image? image = null;
            try
            {
                image = await _thumbnails.GetThumbnailCloneAsync(path, cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch { }
            if (cts.IsCancellationRequested || box.IsDisposed || IsDisposed)
            {
                image?.Dispose();
                return;
            }
            box.Image = image;
        }
    }

    private void ShowSeeAll()
    {
        if (_scannedFiles.Count == 0) return;
        using var viewer = new SeeAllFilesForm(_scannedFiles, _thumbnails);
        viewer.ShowDialog(this);
    }

    // ----- the sort run -----

    private void StopRun() => _runCts?.Cancel();

    private async Task RunSortAsync()
    {
        if (_running || _scannedFiles.Count == 0) return;

        // Never sort a stale scan: the run must act on the folder shown in the Source box.
        var typed = _txtSource.Text.Trim().Trim('"');
        if (!string.Equals(typed, Options.SourceFolder, StringComparison.OrdinalIgnoreCase))
        {
            AdoptSource(typed);
            SetStatus("Source folder changed — scanning; sort again when it finishes.");
            return;
        }
        if (!string.Equals(_scannedFolder, Options.SourceFolder, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Still scanning the source folder — try again in a moment.");
            return;
        }

        _planCts?.Cancel();
        var cts = new CancellationTokenSource();
        _runCts = cts;
        var opts = Options.Clone();
        SetRunningState(true);
        Text = "Photon";
        _runLog.Clear();
        _progressBar.Value = 0;

        SortPlan plan;
        SortResult result;
        try
        {
            SetStatus("Planning...");
            var planProgress = new Progress<SortProgress>(p =>
                SetStatus($"Planning... {p.ProcessedCount:N0}/{p.TotalCount:N0}"));
            plan = await CreatePlanner().BuildPlanAsync(_scannedFiles, opts, planProgress, cts.Token);
            _currentPlan = plan;
            UpdateInfoLine();

            if (!plan.HasEnoughSpace)
            {
                SetStatus("Ready");
                MessageBox.Show(this,
                    $"Not enough space on destination: need {SizeFormatter.Format(plan.RequiredBytes, opts.SizeUnit)}, " +
                    $"free {SizeFormatter.Format(plan.DestinationFreeBytes, opts.SizeUnit)} (plus 200 MB safety margin).",
                    "Not enough space", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetStatus("Sorting...");
            _keepAwake.Start(opts.KeepAwake);
            var progress = new Progress<SortProgress>(UpdateRunProgress);
            result = await CreateExecutor().ExecuteAsync(plan, opts, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendRunLog("Stopped.");
            SetStatus("Stopped");
            return;
        }
        catch (Exception ex)
        {
            AppendRunLog("Failed: " + ex.Message);
            SetStatus("Ready");
            MessageBox.Show(this, "The sort failed:\n\n" + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            _keepAwake.Stop();
            SetRunningState(false);
            if (ReferenceEquals(_runCts, cts)) _runCts = null;
        }

        if (result.Cancelled)
        {
            AppendRunLog($"Stopped. Processed {result.Processed:N0} file(s).");
            SetStatus("Stopped");
            return;
        }

        _progressBar.Value = _progressBar.Maximum;
        AppendRunLog($"Done. Processed {result.Processed:N0} file(s).");
        // Appended after the run so the live progress text no longer overwrites them.
        foreach (var warning in plan.Warnings) AppendRunLog("Warning: " + warning);
        Text = "Finished - Photon";
        SetStatus("Ready");
        _sound.PlayDone(opts.SoundWavPath);

        if (result.Errors.Count > 0)
        {
            var firstTen = string.Join(Environment.NewLine,
                result.Errors.Take(10).Select(er => $"{Path.GetFileName(er.File)}: {er.Error}"));
            MessageBox.Show(this,
                $"{result.Errors.Count:N0} file(s) had errors:\n\n{firstTen}",
                "Completed with errors", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        MessageBox.Show(this, $"Sorted {result.Processed:N0} file(s) into:\n{plan.DestinationRoot}",
            "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        // Read the LIVE setting, not the clone from run start: the status-bar combo stays
        // enabled during a run precisely so the user can change this mid-sort.
        _whenDoneService.Execute(Options.WhenDone, plan.DestinationRoot, this);

        // Source/destination contents changed; refresh unless we're shutting the window.
        if (Options.WhenDone != WhenDoneAction.CloseApp) StartScan();
    }

    private async Task RunPreviewSortAsync()
    {
        if (_running || _scannedFiles.Count == 0) return;
        _planCts?.Cancel();
        var cts = new CancellationTokenSource();
        _runCts = cts;
        var opts = Options.Clone();
        SetRunningState(true);
        _runLog.Clear();
        _progressBar.Value = 0;

        try
        {
            SetStatus("Planning...");
            var planProgress = new Progress<SortProgress>(p =>
                SetStatus($"Planning... {p.ProcessedCount:N0}/{p.TotalCount:N0}"));
            var plan = await CreatePlanner().BuildPlanAsync(_scannedFiles, opts, planProgress, cts.Token);

            SetStatus("Building ghost preview...");
            var progress = new Progress<SortProgress>(UpdateRunProgress);
            var service = new PreviewSortService();
            var result = await service.RunPreviewAsync(plan, progress, cts.Token);

            if (result.Cancelled)
            {
                AppendRunLog("Preview stopped.");
                SetStatus("Stopped");
                return;
            }
            AppendRunLog($"Ghost preview done. {result.Processed:N0} file(s).");
            SetStatus("Ready");
            MessageBox.Show(this,
                $"Ghost preview created for {result.Processed:N0} file(s) under:\n{plan.DestinationRoot}\n\n" +
                "Nothing was copied or moved — the preview contains shortcuts only.",
                "Preview sort", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            AppendRunLog("Preview stopped.");
            SetStatus("Stopped");
        }
        catch (Exception ex)
        {
            SetStatus("Ready");
            MessageBox.Show(this, "The preview failed:\n\n" + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRunningState(false);
            if (ReferenceEquals(_runCts, cts)) _runCts = null;
        }
    }

    private void SetRunningState(bool running)
    {
        _running = running;
        _grpFolders.Enabled = !running;
        _tabs.Enabled = !running;
        _chkKeepAwakeRun.Enabled = !running;
        _btnStop.Enabled = running;
        _miStop.Enabled = running;
        _miUndo.Enabled = !running;
        _miHistory.Enabled = !running;
        _miBatchRename.Enabled = !running;
        _miDupFinder.Enabled = !running;
        _miFolderScan.Enabled = !running;
        _miFlatten.Enabled = !running;
        _miRefreshPreview.Enabled = !running;
        _miResetSettings.Enabled = !running;
        UpdateSortAvailability();
    }

    private void UpdateRunProgress(SortProgress p)
    {
        if (IsDisposed || !IsHandleCreated) return;
        var unit = Options.SizeUnit;
        var sb = new StringBuilder();
        sb.AppendLine(Path.GetFileName(p.CurrentFile));
        sb.AppendLine($"Estimated time remaining: {FormatEta(p.EstimatedRemaining)}");
        sb.AppendLine($"Data: Total {SizeFormatter.Format(p.TotalBytes, unit)} | " +
                      $"Completed {SizeFormatter.Format(p.ProcessedBytes, unit)} | " +
                      $"Left {SizeFormatter.Format(Math.Max(0, p.TotalBytes - p.ProcessedBytes), unit)}");
        sb.Append($"Speed: {p.FilesPerSecond:0.00} files/s | {SizeFormatter.FormatRate(p.BytesPerSecond)}");
        _runLog.Text = sb.ToString();

        if (p.TotalCount > 0)
        {
            _progressBar.Maximum = p.TotalCount;
            _progressBar.Value = Math.Clamp(p.ProcessedCount, 0, p.TotalCount);
        }
        SetStatus($"Sorting... {p.ProcessedCount:N0}/{p.TotalCount:N0}");
    }

    private static string FormatEta(TimeSpan? eta)
    {
        if (eta is not { } t || t < TimeSpan.Zero) return "estimating...";
        if (t.TotalSeconds < 120) return $"{(int)Math.Ceiling(t.TotalSeconds)}s";
        if (t.TotalMinutes < 120) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        return $"{(int)t.TotalHours}h {t.Minutes}m";
    }

    private void AppendRunLog(string line)
    {
        if (_runLog.TextLength > 0) _runLog.AppendText(Environment.NewLine);
        _runLog.AppendText(line);
    }

    private void SetStatus(string text)
    {
        if (IsDisposed) return;
        _lblStatus.Text = text;
    }

    // ----- menu actions -----

    private void RebuildRecentMenu()
    {
        _miRecent.DropDownItems.Clear();
        if (Settings.RecentSources.Count == 0)
        {
            _miRecent.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false });
            return;
        }
        foreach (var folder in Settings.RecentSources)
        {
            var item = new ToolStripMenuItem(folder) { Tag = folder };
            item.Click += (s, _) => AdoptSource((string)((ToolStripMenuItem)s!).Tag!);
            _miRecent.DropDownItems.Add(item);
        }
    }

    private void OpenOutputFolder()
    {
        if (string.IsNullOrWhiteSpace(Options.SourceFolder) && string.IsNullOrWhiteSpace(Options.OutputFolder))
        {
            MessageBox.Show(this, "Select a source folder first.", "Photon",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var root = Options.ResolveOutputFolder();
        if (Directory.Exists(root)) WhenDoneService.OpenFolder(root);
        else MessageBox.Show(this, $"The output folder does not exist yet:\n{root}", "Photon",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CopyLog()
    {
        if (_runLog.TextLength == 0) return;
        try { Clipboard.SetText(_runLog.Text); } catch { }
    }

    private void ResetSettings()
    {
        if (MessageBox.Show(this,
                "Reset all settings to their defaults?\nRecent sources will also be cleared.",
                "Reset settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _settingsService.ResetToDefaults();
        _settingsService.Save();
        ApplySettingsToUi();
    }

    private void ResetExtensions()
    {
        _suppress = true;
        _txtCustomExt.Text = "";
        _chkPictures.Checked = true;
        _chkVideos.Checked = true;
        _suppress = false;
        OnScanOptionChanged(_btnResetExt, EventArgs.Empty);
    }

    private void UndoLastSort()
    {
        HistoryForm.UndoLatest(this);
        StartScan();
    }

    private void ShowHistory()
    {
        using var form = new HistoryForm();
        form.ShowDialog(this);
        StartScan();
    }

    private string? CurrentSourceOrNull() =>
        Directory.Exists(Options.SourceFolder) ? Options.SourceFolder : null;

    private void ShowBatchRename()
    {
        using var form = new BatchRenameForm(CurrentSourceOrNull());
        form.ShowDialog(this);
        StartScan();
    }

    private void ShowDuplicateFinder()
    {
        using var form = new DuplicateFinderForm(CurrentSourceOrNull());
        form.ShowDialog(this);
        StartScan();
    }

    private void ShowFolderScan()
    {
        using var form = new FolderScanForm(CurrentSourceOrNull());
        form.SourceChosen += folder => AdoptSource(folder);
        form.ShowDialog(this);
    }

    private void ShowFlatten()
    {
        using var form = new FlattenForm(CurrentSourceOrNull());
        form.ShowDialog(this);
        StartScan();
    }

    private void ShowDriveInspector()
    {
        using var form = new DriveInspectorForm();
        form.ShowDialog(this);
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"Photon {Application.ProductVersion}\n\n" +
            "A fast picture & video sorter: by date, by camera, with duplicate handling,\n" +
            "ghost previews, and full undo via the journal history.",
            "About Photon", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ----- status clock -----

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var greeting = now.Hour switch
        {
            >= 5 and < 11 => "Good morning",
            >= 11 and < 17 => "Good afternoon",
            >= 17 and < 22 => "Good evening",
            _ => "Good night",
        };
        _lblClock.Text =
            $"{greeting} · {now.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture)} · {now:HH:mm:ss}";
    }

    // ----- window lifecycle -----

    private void ApplyWindowPlacement()
    {
        if (Settings.WindowBounds is not { Width: >= 400, Height: >= 300 } wp) return;
        var rect = wp.ToRectangle();
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = rect;
        if (wp.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_running && e.CloseReason == CloseReason.UserClosing)
        {
            if (MessageBox.Show(this, "A sort is running. Stop it and exit?", "Photon",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        _scanCts?.Cancel();
        _planCts?.Cancel();
        _thumbCts?.Cancel();
        _runCts?.Cancel();
        _keepAwake.Stop();

        var maximized = WindowState == FormWindowState.Maximized;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        Settings.WindowBounds = WindowPlacement.From(bounds, maximized);
        _settingsService.Save();
    }
}
