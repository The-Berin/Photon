using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ComIPersistFile = System.Runtime.InteropServices.ComTypes.IPersistFile;

namespace Photon.App.Interop;

/// <summary>
/// Minimal IShellLinkW COM interop for creating .lnk shortcuts without a WSH/COM-reference dependency.
/// Used by the preview ("ghost") sort to mirror the planned tree with shortcuts.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShellLink
{
    /// <summary>Creates (or overwrites) a shortcut at <paramref name="lnkPath"/> pointing at <paramref name="targetPath"/>.</summary>
    public static void CreateShortcut(string lnkPath, string targetPath, string? description)
    {
        var link = (IShellLinkW)new ShellLinkObject();
        try
        {
            link.SetPath(targetPath);
            var workingDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(workingDir)) link.SetWorkingDirectory(workingDir);
            if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
            ((ComIPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    // ShellLink CoClass — CLSID_ShellLink.
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkObject;

    // Member order must match the shobjidl_core.h vtable exactly; unused members still occupy slots.
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
