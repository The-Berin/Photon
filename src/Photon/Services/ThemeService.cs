using Photon.Core.Models;

namespace Photon.App.Services;

/// <summary>
/// Dark mode. The real switch is <see cref="Application.SetColorMode"/>, which only takes
/// effect before the first window is created — so it runs from Program.cs at startup, and
/// a runtime toggle gets an honest best-effort recolor plus a "restart to fully apply" note.
/// </summary>
public static class ThemeService
{
    private static readonly Color DarkBack = Color.FromArgb(32, 32, 32);
    private static readonly Color DarkSurface = Color.FromArgb(45, 45, 48);
    private static readonly Color DarkField = Color.FromArgb(60, 60, 64);
    private static readonly Color DarkText = Color.FromArgb(230, 230, 230);

    /// <summary>Startup-only: must run before the first form is shown.</summary>
    public static void ApplyColorMode(AppTheme theme)
    {
        if (!OperatingSystem.IsWindows()) return;
        Application.SetColorMode(theme switch
        {
            AppTheme.Dark => SystemColorMode.Dark,
            AppTheme.Light => SystemColorMode.Classic,
            _ => SystemColorMode.System,
        });
    }

    /// <summary>Best-effort immediate recolor for a runtime toggle; a restart applies the native mode.</summary>
    public static void ApplyBestEffort(Control root, bool dark)
    {
        Recolor(root, dark);
        root.Invalidate(true);
    }

    private static void Recolor(Control control, bool dark)
    {
        switch (control)
        {
            case TextBoxBase or ListBox or ListView or ComboBox or TreeView:
                control.BackColor = dark ? DarkField : SystemColors.Window;
                control.ForeColor = dark ? DarkText : SystemColors.WindowText;
                break;
            case Button button:
                button.BackColor = dark ? DarkSurface : SystemColors.Control;
                button.ForeColor = dark ? DarkText : SystemColors.ControlText;
                button.UseVisualStyleBackColor = !dark;
                break;
            case MenuStrip or StatusStrip or ToolStrip:
                control.BackColor = dark ? DarkSurface : SystemColors.Control;
                control.ForeColor = dark ? DarkText : SystemColors.ControlText;
                break;
            case Form:
                control.BackColor = dark ? DarkBack : SystemColors.Control;
                control.ForeColor = dark ? DarkText : SystemColors.ControlText;
                break;
            default:
                control.BackColor = dark ? DarkBack : SystemColors.Control;
                control.ForeColor = dark ? DarkText : SystemColors.ControlText;
                break;
        }
        foreach (Control child in control.Controls)
            Recolor(child, dark);
    }
}
