using System.Diagnostics;

namespace Photon.App.Interop;

/// <summary>Best-effort Explorer launch helpers shared by the tool windows.</summary>
public static class ExplorerShell
{
    /// <summary>Opens an Explorer window at the given folder.</summary>
    public static void OpenFolder(string folder)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
        catch { /* opening Explorer is a courtesy, never a failure */ }
    }

    /// <summary>Opens an Explorer window with the given file selected.</summary>
    public static void RevealFile(string filePath)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true }); }
        catch { /* best effort */ }
    }

    /// <summary>Opens a file with its default application.</summary>
    public static void OpenFile(string filePath)
    {
        try { Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true }); }
        catch { /* best effort — e.g. no associated app */ }
    }
}
