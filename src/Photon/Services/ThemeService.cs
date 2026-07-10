using Photon.Core.Models;

namespace Photon.App.Services;

/// <summary>
/// Native-first theming. The real switch is <see cref="Application.SetColorMode"/>, which only
/// takes effect before the first window is created — so it runs from Program.cs at startup and
/// a runtime toggle just saves the setting and shows a one-time "restart to fully apply" note.
/// .NET 9's native dark mode themes almost everything (including adaptive SystemColors), but
/// leaves known gaps: TabControl headers, DataGridView, ToolStripItem text in strips, and
/// ToolStripComboBox. <see cref="FixGaps"/> patches exactly those, and only when dark is
/// actually active — light mode stays 100% native.
/// </summary>
public static class ThemeService
{
    // ----- palette (dark mode only; light mode never touches colors) -----

    private static readonly Color DarkBack = Color.FromArgb(32, 32, 32);
    private static readonly Color DarkSurface = Color.FromArgb(45, 45, 48);
    private static readonly Color DarkField = Color.FromArgb(60, 60, 64);
    private static readonly Color DarkText = Color.FromArgb(230, 230, 230);
    private static readonly Color DarkBorder = Color.FromArgb(85, 85, 90);
    private static readonly Color DarkGridLine = Color.FromArgb(72, 72, 76);
    private static readonly Color DarkSelection = Color.FromArgb(66, 96, 138);

    /// <summary>True when the app is effectively running in dark mode.</summary>
    public static bool IsDark => OperatingSystem.IsWindows() && Application.IsDarkModeEnabled;

    // ----- semantic colors used by forms; each pair keeps the classic light value verbatim -----

    /// <summary>"Fits / OK" verdict text.</summary>
    public static Color SuccessText => IsDark ? Color.FromArgb(130, 200, 130) : Color.Green;

    /// <summary>"Does not fit / error" verdict text.</summary>
    public static Color ErrorText => IsDark ? Color.FromArgb(235, 130, 130) : Color.Firebrick;

    /// <summary>Row tint for "this file will change" grid rows.</summary>
    public static Color GridChangedBack => IsDark ? Color.FromArgb(42, 72, 46) : Color.FromArgb(223, 240, 216);

    /// <summary>Row/cell tint for problem rows and invalid-regex cells.</summary>
    public static Color GridProblemBack => IsDark ? Color.FromArgb(88, 44, 48) : Color.FromArgb(248, 215, 218);

    /// <summary>Status text on problem rows (light red on dark, dark red on light).</summary>
    public static Color GridProblemText => IsDark ? Color.FromArgb(240, 160, 160) : Color.FromArgb(150, 30, 30);

    /// <summary>De-emphasized text for excluded/off rows.</summary>
    public static Color GridDimText => IsDark ? Color.FromArgb(150, 150, 150) : Color.Gray;

    /// <summary>
    /// Startup-only: must run before the first form is shown. Light (Classic) is the default
    /// look regardless of the OS theme — WinForms native dark rendering is still rough, so
    /// dark is strictly opt-in via the Dark mode checkbox (AppTheme.System maps to light too).
    /// </summary>
    public static void ApplyColorMode(AppTheme theme)
    {
        if (!OperatingSystem.IsWindows()) return;
        Application.SetColorMode(theme == AppTheme.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
    }

    /// <summary>
    /// Called once from every form's constructor, after its controls are built.
    /// No-op in light mode; in dark mode it patches the controls native dark mode misses.
    /// </summary>
    public static void FixGaps(Form form)
    {
        // Every Photon window calls FixGaps exactly once from its constructor, which makes
        // this the single cheapest hook to stamp the app icon on all of them too.
        AppIcon.Apply(form);
        if (!IsDark) return;
        // Drop-downs and context menus default to ManagerRenderMode, so the shared dark
        // renderer covers them even though they never appear in a form's control tree.
        ToolStripManager.Renderer = DarkStripRenderer;
        PatchTree(form);
    }

    private static void PatchTree(Control control)
    {
        switch (control)
        {
            case TabControl tabs:
                PatchTabControl(tabs);
                break;
            case DataGridView grid:
                PatchDataGridView(grid);
                break;
            case ToolStrip strip: // covers MenuStrip and StatusStrip too
                PatchToolStrip(strip);
                break;
            case ListView list:
                list.BackColor = DarkField;
                list.ForeColor = DarkText;
                // Column headers and ListViewGroup header text are drawn by the native control
                // from its visual style and ignore ForeColor. Best effort: ask the control to use
                // the OS dark explorer theme (idempotent if .NET 9 already applied it). Handles
                // don't exist yet in form constructors, so hook creation (covers recreation too).
                list.HandleCreated += static (s, _) =>
                    SetWindowTheme(((ListView)s!).Handle, "DarkMode_Explorer", null);
                if (list.IsHandleCreated)
                    SetWindowTheme(list.Handle, "DarkMode_Explorer", null);
                break;
            case NumericUpDown numeric:
                // The text region follows adaptive SystemColors, but the native spinner button
                // pair is a known partially-themed gap and can render as small light squares.
                numeric.BackColor = DarkField;
                numeric.ForeColor = DarkText;
                break;
            case LinkLabel link:
                link.LinkColor = Color.FromArgb(120, 180, 255);
                link.ActiveLinkColor = Color.FromArgb(170, 210, 255);
                link.VisitedLinkColor = Color.FromArgb(185, 160, 240);
                break;
        }
        foreach (Control child in control.Controls)
            PatchTree(child);
    }

    // ----- TabControl: native dark mode leaves the header strip light -----

    private static void PatchTabControl(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.Padding = new Point(12, 4); // a little room so owner-drawn header text sits clean
        tabs.DrawItem += OnDrawTabItem;
        // Widening the window exposes new header area right of the last tab that the native
        // control paints light; the last tab's own rectangle isn't invalidated, so DrawItem
        // (which paints that filler) may not re-fire. Force a full repaint on resize.
        tabs.Resize += static (s, _) => ((TabControl)s!).Invalidate();
        foreach (TabPage page in tabs.TabPages)
        {
            // Visual-style page backgrounds render light regardless of color mode.
            page.UseVisualStyleBackColor = false;
            page.BackColor = DarkBack;
        }
    }

    private static void OnDrawTabItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount) return;

        var selected = e.Index == tabs.SelectedIndex;
        using (var back = new SolidBrush(selected ? DarkSurface : DarkBack))
            e.Graphics.FillRectangle(back, e.Bounds);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds,
            selected ? DarkText : Color.FromArgb(190, 190, 190),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // The strip filler to the right of the last tab is outside every tab's bounds and
        // would otherwise stay light; the Graphics here spans the whole control, so paint it.
        if (e.Index == tabs.TabCount - 1)
        {
            var last = tabs.GetTabRect(tabs.TabCount - 1);
            var filler = new Rectangle(last.Right, 0, tabs.Width - last.Right, last.Bottom);
            if (filler.Width > 0)
            {
                using var fillerBack = new SolidBrush(DarkBack);
                e.Graphics.FillRectangle(fillerBack, filler);
            }
        }
    }

    // ----- DataGridView: fully unthemed by native dark mode -----

    private static void PatchDataGridView(DataGridView grid)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = DarkBack;
        grid.GridColor = DarkGridLine;

        grid.DefaultCellStyle.BackColor = DarkField;
        grid.DefaultCellStyle.ForeColor = DarkText;
        grid.DefaultCellStyle.SelectionBackColor = DarkSelection;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(66, 66, 70); // a touch lighter

        grid.ColumnHeadersDefaultCellStyle.BackColor = DarkSurface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = DarkText;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = DarkSurface;
        grid.RowHeadersDefaultCellStyle.BackColor = DarkSurface;
        grid.RowHeadersDefaultCellStyle.ForeColor = DarkText;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = DarkSelection;
    }

    // ----- MenuStrip / StatusStrip / ToolStrip: item text and combo islands -----

    private static readonly ToolStripProfessionalRenderer DarkStripRenderer = new DarkRenderer();

    private static void PatchToolStrip(ToolStrip strip)
    {
        strip.Renderer = DarkStripRenderer;
        foreach (ToolStripItem item in strip.Items)
            PatchStripItem(item);
    }

    private static void PatchStripItem(ToolStripItem item)
    {
        item.ForeColor = DarkText;
        if (item is ToolStripComboBox combo)
        {
            // Stops the combo rendering as a white island inside the dark strip.
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = DarkField;
            combo.ForeColor = DarkText;
        }
        if (item is ToolStripDropDownItem dropDown)
            foreach (ToolStripItem child in dropDown.DropDownItems)
                PatchStripItem(child);
    }

    /// <summary>
    /// Professional renderer with the dark color table, plus an owner-drawn menu check glyph:
    /// the stock renderer stamps its default dark checkmark bitmap over CheckBackground, and
    /// with a dark table that is dark-on-dark — so draw the glyph ourselves in DarkText.
    /// </summary>
    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColorTable()) => RoundedEdges = false;

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using (var back = new SolidBrush(DarkField))
                e.Graphics.FillRectangle(back, r);

            var oldMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(DarkText, Math.Max(1.5f, r.Height / 8f)))
            {
                e.Graphics.DrawLines(pen,
                [
                    new PointF(r.Left + r.Width * 0.24f, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.43f, r.Top + r.Height * 0.74f),
                    new PointF(r.Left + r.Width * 0.76f, r.Top + r.Height * 0.30f),
                ]);
            }
            e.Graphics.SmoothingMode = oldMode;
        }
    }

    // ----- native ListView theming (uxtheme) -----

    [System.Runtime.InteropServices.DllImport("uxtheme.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    /// <summary>Small dark color table for menus, status bars, and tool strips.</summary>
    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => DarkSurface;
        public override Color MenuStripGradientEnd => DarkSurface;
        public override Color StatusStripGradientBegin => DarkSurface;
        public override Color StatusStripGradientEnd => DarkSurface;
        public override Color ToolStripGradientBegin => DarkSurface;
        public override Color ToolStripGradientMiddle => DarkSurface;
        public override Color ToolStripGradientEnd => DarkSurface;
        public override Color ToolStripDropDownBackground => DarkSurface;
        public override Color ToolStripBorder => DarkBorder;
        public override Color ToolStripContentPanelGradientBegin => DarkSurface;
        public override Color ToolStripContentPanelGradientEnd => DarkSurface;

        public override Color ImageMarginGradientBegin => DarkSurface;
        public override Color ImageMarginGradientMiddle => DarkSurface;
        public override Color ImageMarginGradientEnd => DarkSurface;

        public override Color MenuBorder => DarkBorder;
        public override Color MenuItemBorder => DarkBorder;
        public override Color MenuItemSelected => DarkField;
        public override Color MenuItemSelectedGradientBegin => DarkField;
        public override Color MenuItemSelectedGradientEnd => DarkField;
        public override Color MenuItemPressedGradientBegin => DarkField;
        public override Color MenuItemPressedGradientMiddle => DarkField;
        public override Color MenuItemPressedGradientEnd => DarkField;

        public override Color ButtonSelectedBorder => DarkBorder;
        public override Color ButtonSelectedHighlight => DarkField;
        public override Color ButtonSelectedGradientBegin => DarkField;
        public override Color ButtonSelectedGradientMiddle => DarkField;
        public override Color ButtonSelectedGradientEnd => DarkField;
        public override Color ButtonPressedHighlight => DarkBorder;
        public override Color ButtonPressedGradientBegin => DarkBorder;
        public override Color ButtonPressedGradientMiddle => DarkBorder;
        public override Color ButtonPressedGradientEnd => DarkBorder;

        public override Color CheckBackground => DarkField;
        public override Color CheckSelectedBackground => DarkBorder;
        public override Color CheckPressedBackground => DarkBorder;

        public override Color SeparatorDark => DarkBorder;
        public override Color SeparatorLight => DarkSurface;
        public override Color GripDark => DarkBorder;
        public override Color GripLight => DarkSurface;
    }
}
