using System.Diagnostics;
using Photon.Core.Models;

namespace Photon.Core.Services;

/// <summary>
/// Best-effort incremental journal persistence during a run. The journal is the undo
/// system's source of truth, so a process kill or power loss mid-run must not lose the
/// record of operations that already happened. Saves are throttled (every few seconds
/// or every batch of new entries); the run's final authoritative save happens separately.
/// </summary>
internal sealed class JournalCheckpoint(IJournalService journals, SortJournal journal)
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(2);
    private const int EntriesPerSave = 25;

    private readonly Stopwatch _sinceSave = Stopwatch.StartNew();
    private int _savedEntryCount;

    /// <summary>Persists the journal now. Failures are swallowed: a checkpoint hiccup must never abort the run.</summary>
    public void SaveNow()
    {
        try
        {
            journals.SaveAsync(journal, CancellationToken.None).GetAwaiter().GetResult();
            _savedEntryCount = journal.Entries.Count;
            _sinceSave.Restart();
        }
        catch
        {
            // Best effort — the final save reports persistence errors to the caller.
        }
    }

    /// <summary>Persists when enough new entries or time have accumulated since the last save.</summary>
    public void MaybeSave()
    {
        var newEntries = journal.Entries.Count - _savedEntryCount;
        if (newEntries == 0) return;
        // The very first entry saves immediately — the journal (and its backup folder)
        // becomes discoverable as soon as anything happened; later saves are throttled.
        if (_savedEntryCount > 0 && newEntries < EntriesPerSave && _sinceSave.Elapsed < SaveInterval) return;
        SaveNow();
    }
}
