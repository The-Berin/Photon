using System.Text.Json;
using System.Text.Json.Serialization;

namespace Photon.App.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as JSON. A corrupt or missing file silently
/// falls back to defaults — settings must never prevent the app from starting.
/// </summary>
public sealed class SettingsService
{
    public const int MaxRecentSources = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string SettingsPath { get; }
    public AppSettings Current { get; private set; } = new();

    public SettingsService(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Photon", "settings.json");
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            if (loaded is null) return;
            // Null-out anything a hand-edited file may have broken.
            loaded.Options ??= new();
            loaded.RecentSources ??= [];
            Current = loaded;
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch
        {
            // Non-fatal: settings just won't persist this time.
        }
    }

    public void AddRecentSource(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        folder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (folder.Length == 0) return;
        Current.RecentSources.RemoveAll(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase));
        Current.RecentSources.Insert(0, folder);
        if (Current.RecentSources.Count > MaxRecentSources)
            Current.RecentSources.RemoveRange(MaxRecentSources, Current.RecentSources.Count - MaxRecentSources);
    }

    public void ResetToDefaults() => Current = new AppSettings();
}
