using Photon.Core.Models;

namespace Photon.App.Services;

/// <summary>Everything Photon persists between sessions (JSON at %APPDATA%\Photon\settings.json).</summary>
public sealed class AppSettings
{
    public SortOptions Options { get; set; } = new();
    public WindowPlacement? WindowBounds { get; set; }
    public List<string> RecentSources { get; set; } = [];
    public bool AlwaysOnTop { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Light;
    /// <summary>Status-bar clock format; off = 12-hour with AM/PM.</summary>
    public bool Use24HourClock { get; set; }
}

/// <summary>Saved main-window placement, kept as plain ints so the JSON stays trivial.</summary>
public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }

    public Rectangle ToRectangle() => new(X, Y, Width, Height);

    public static WindowPlacement From(Rectangle bounds, bool maximized) => new()
    {
        X = bounds.X,
        Y = bounds.Y,
        Width = bounds.Width,
        Height = bounds.Height,
        Maximized = maximized,
    };
}
