using System.Runtime.InteropServices;

namespace Photon.App.Services;

/// <summary>
/// Prevents Windows from sleeping while a sort runs, via SetThreadExecutionState.
/// The flags are thread-affine, so Start/Stop must always run on the same (UI) thread.
/// </summary>
public sealed class KeepAwakeService
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    public bool IsActive { get; private set; }

    /// <summary>System always stays awake during a run; the display only when the user asked.</summary>
    public void Start(bool keepDisplayOn)
    {
        if (!OperatingSystem.IsWindows()) return;
        var flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED;
        if (keepDisplayOn) flags |= ES_DISPLAY_REQUIRED;
        SetThreadExecutionState(flags);
        IsActive = true;
    }

    public void Stop()
    {
        if (!IsActive || !OperatingSystem.IsWindows()) return;
        SetThreadExecutionState(ES_CONTINUOUS);
        IsActive = false;
    }
}
