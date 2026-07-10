using System.Text;
using Photon.App.Interop;
using Photon.Core.Models;
using Photon.Core.Services;

namespace Photon.App.Forms;

/// <summary>
/// Feature 5: the undo center — every journaled operation newest first,
/// with per-entry undo and quick access to the journal folder and destinations.
/// </summary>
public sealed class HistoryForm : Form
{
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, ShowItemToolTips = true,
    };
    private readonly Button _undoButton = new() { Text = "Undo selected", AutoSize = true };
    private readonly Button _openJournalButton = new() { Text = "Open journal folder", AutoSize = true };
    private readonly Button _openDestButton = new() { Text = "Open destination", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Label _statusLabel = new()
    {
        AutoSize = false, Height = 20, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
    };

    public HistoryForm()
    {
        Text = "History";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1020, 560);
        MinimumSize = new Size(800, 400);

        _list.Columns.Add("When", 130);
        _list.Columns.Add("Kind", 110);
        _list.Columns.Add("Action", 70);
        _list.Columns.Add("Source → Destination", 440);
        _list.Columns.Add("Files", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Status", 150);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange([_undoButton, _openJournalButton, _openDestButton, _refreshButton]);
        root.Controls.Add(buttons, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(root);

        _refreshButton.Click += async (_, _) => await RefreshJournalsAsync();
        _undoButton.Click += OnUndoSelected;
        _openJournalButton.Click += OnOpenJournalFolder;
        _openDestButton.Click += OnOpenDestination;
        Load += async (_, _) => await RefreshJournalsAsync();
    }

    private async Task RefreshJournalsAsync()
    {
        _statusLabel.Text = "Loading journals…";
        List<SortJournal> journals;
        try
        {
            journals = await Task.Run(() => new JournalService().LoadAll());
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Failed to load journals: " + ex.Message;
            return;
        }
        if (IsDisposed) return;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var j in journals)
        {
            bool undone = j.UndoneAtUtc is not null;
            var item = new ListViewItem(j.TimestampUtc.ToLocalTime().ToString("g")) { Tag = j };
            item.SubItems.Add(j.OperationKind);
            item.SubItems.Add(j.Action.ToString());
            item.SubItems.Add($"{j.SourceFolder} → {j.DestinationRoot}");
            item.SubItems.Add(j.Entries.Count.ToString("N0"));
            item.SubItems.Add(undone ? $"Undone {j.UndoneAtUtc!.Value.ToLocalTime():g}" : "Undoable");
            if (undone) item.ForeColor = SystemColors.GrayText;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        _statusLabel.Text = $"{journals.Count:N0} operations on record.";
    }

    private SortJournal? SelectedJournal() =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as SortJournal : null;

    private async void OnUndoSelected(object? sender, EventArgs e)
    {
        var journal = SelectedJournal();
        if (journal is null)
        {
            MessageBox.Show(this, "Select an operation to undo.", "History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (journal.UndoneAtUtc is not null)
        {
            MessageBox.Show(this, "That operation was already undone.", "History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var confirm = MessageBox.Show(this,
            $"Undo this operation?\n\n" +
            $"{journal.OperationKind} — {journal.TimestampUtc.ToLocalTime():g}\n" +
            $"{journal.Entries.Count:N0} file entries\n" +
            $"{journal.SourceFolder} → {journal.DestinationRoot}\n\n" +
            "Files will be returned to their original locations.",
            "Undo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        RunUndoWithDialog(this, journal);
        await RefreshJournalsAsync();
    }

    private void OnOpenJournalFolder(object? sender, EventArgs e)
    {
        try
        {
            ExplorerShell.OpenFolder(new JournalService().JournalDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "History", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnOpenDestination(object? sender, EventArgs e)
    {
        var journal = SelectedJournal();
        if (journal is null)
        {
            MessageBox.Show(this, "Select an operation first.", "History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Directory.Exists(journal.DestinationRoot))
        {
            MessageBox.Show(this, "The destination folder no longer exists:\n" + journal.DestinationRoot,
                "History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ExplorerShell.OpenFolder(journal.DestinationRoot);
    }

    /// <summary>
    /// One-click "undo the last thing Photon did" entry point for the main window.
    /// Confirms, runs with a small progress dialog, then shows the outcome.
    /// </summary>
    public static void UndoLatest(IWin32Window owner)
    {
        SortJournal? journal;
        try
        {
            journal = new JournalService().LoadLatestUndoable();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Could not read the journal folder:\n" + ex.Message,
                "Undo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (journal is null)
        {
            MessageBox.Show(owner, "Nothing to undo — there are no undoable operations on record.",
                "Undo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var confirm = MessageBox.Show(owner,
            $"Undo the most recent operation?\n\n" +
            $"{journal.OperationKind} — {journal.TimestampUtc.ToLocalTime():g}\n" +
            $"{journal.Entries.Count:N0} file entries\n" +
            $"{journal.SourceFolder} → {journal.DestinationRoot}",
            "Undo", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        RunUndoWithDialog(owner, journal);
    }

    private static void RunUndoWithDialog(IWin32Window owner, SortJournal journal)
    {
        using var dialog = new UndoProgressDialog(journal);
        dialog.ShowDialog(owner);
        if (dialog.Error is not null)
        {
            MessageBox.Show(owner, "Undo failed:\n" + dialog.Error.Message,
                "Undo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (dialog.Result is not { } r) return;

        var sb = new StringBuilder();
        if (r.Cancelled) sb.AppendLine("Undo was cancelled part-way — remaining entries are untouched.").AppendLine();
        sb.AppendLine($"Reversed: {r.Reversed:N0}");
        sb.AppendLine($"Restored from backup: {r.RestoredFromBackup:N0}");
        sb.AppendLine($"Empty folders removed: {r.DirectoriesRemoved:N0}");
        if (r.Errors.Count > 0)
        {
            sb.AppendLine($"Errors: {r.Errors.Count:N0}");
            foreach (var (file, error) in r.Errors.Take(5))
                sb.AppendLine($"  {Path.GetFileName(file)}: {error}");
            if (r.Errors.Count > 5) sb.AppendLine("  …");
        }
        MessageBox.Show(owner, sb.ToString(), "Undo", MessageBoxButtons.OK,
            r.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    /// <summary>Small modal dialog that runs UndoAsync off the UI thread with progress and cancel.</summary>
    private sealed class UndoProgressDialog : Form
    {
        public UndoResult? Result { get; private set; }
        public Exception? Error { get; private set; }

        private readonly SortJournal _journal;
        private readonly CancellationTokenSource _cts = new();
        private readonly Label _label = new() { AutoEllipsis = true };
        private readonly ProgressBar _bar = new();
        private readonly Button _cancelButton = new() { Text = "Cancel" };

        public UndoProgressDialog(SortJournal journal)
        {
            _journal = journal;
            Text = "Undoing…";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 108);

            _label.SetBounds(12, 12, 416, 18);
            _label.Text = $"Undoing \"{_journal.OperationKind}\" — {_journal.Entries.Count:N0} entries…";
            _bar.SetBounds(12, 38, 416, 16);
            _cancelButton.SetBounds(340, 66, 88, 28);
            _cancelButton.Click += (_, _) => { _cancelButton.Enabled = false; _cts.Cancel(); };
            Controls.AddRange([_label, _bar, _cancelButton]);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            var progress = new UiProgress(this, p =>
            {
                if (p.TotalCount > 0)
                    _bar.Value = Math.Clamp((int)(p.ProcessedCount * 100L / p.TotalCount), 0, 100);
                _label.Text = $"{p.ProcessedCount:N0}/{Math.Max(p.TotalCount, p.ProcessedCount):N0} — {Path.GetFileName(p.CurrentFile)}";
            });
            try
            {
                Result = await Task.Run(() => new JournalService().UndoAsync(_journal, progress, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                Result = new UndoResult { Cancelled = true };
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                _cts.Dispose();
                DialogResult = DialogResult.OK;
                Close();
            }
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
