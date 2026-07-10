using System.Diagnostics;
using System.Runtime.InteropServices;
using Photon.Core.Models;

namespace Photon.App.Services;

/// <summary>Runs the post-sort action the user picked in the "When Done" combo.</summary>
public sealed class WhenDoneService
{
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public void Execute(WhenDoneAction action, string outputRoot, Form owner)
    {
        switch (action)
        {
            case WhenDoneAction.OpenOutputFolder:
                OpenFolder(outputRoot);
                break;

            case WhenDoneAction.CloseApp:
                owner.Close();
                break;

            case WhenDoneAction.Sleep:
                if (OperatingSystem.IsWindows())
                    SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
                break;

            case WhenDoneAction.Shutdown:
                MessageBox.Show(owner,
                    "Windows will shut down in 30 seconds.\n\nTo abort, open a command prompt and run: shutdown /a",
                    "Shutting down", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("shutdown", "/s /t 30")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });
                    }
                    catch { }
                }
                break;
        }
    }

    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", path);
        }
        catch { }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
