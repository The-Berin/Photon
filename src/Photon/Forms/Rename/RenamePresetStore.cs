using System.Text.Json;
using System.Text.Json.Serialization;
using Photon.Core.Models;

namespace Photon.App.Forms;

/// <summary>
/// Persists user rename presets as a name → RenameOptions dictionary in
/// %APPDATA%\Photon\rename-presets.json, and ships the built-in presets.
/// </summary>
internal sealed class RenamePresetStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Photon", "rename-presets.json");

    public Dictionary<string, RenameOptions> User { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, RenameOptions> BuiltIn = new Dictionary<string, RenameOptions>
    {
        ["Date + counter (IMG_2024-06-01_001)"] = new()
        {
            Pattern = "IMG_{yyyy}-{MM}-{dd}_{counter}",
            CounterStart = 1, CounterStep = 1, CounterPadding = 3,
            ExtensionCase = CaseTransform.Lower,
        },
        ["Clean camera names"] = new()
        {
            Pattern = "{name}",
            Replacements =
            [
                new FindReplaceRule { Find = @"^(IMG|DSC|DSCN|MVI|VID|PXL|DJI)[_-]", Replace = "", UseRegex = true },
                new FindReplaceRule { Find = "_", Replace = " " },
            ],
            TrimWhitespace = true, CollapseSpaces = true,
        },
        ["Lowercase everything"] = new()
        {
            Pattern = "{name}",
            NameCase = CaseTransform.Lower,
            ExtensionCase = CaseTransform.Lower,
        },
        ["Strip bracket junk"] = new()
        {
            Pattern = "{name}",
            RemoveBracketedText = true, TrimWhitespace = true, CollapseSpaces = true,
        },
        ["Camera + megapixels"] = new()
        {
            Pattern = "{camera}_{mp}MP_{yyyy}-{MM}-{dd}_{counter}",
            CounterPadding = 3, ExtensionCase = CaseTransform.Lower,
        },
    };

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { User = new(StringComparer.OrdinalIgnoreCase); return; }
            var loaded = JsonSerializer.Deserialize<Dictionary<string, RenameOptions>>(File.ReadAllText(FilePath), JsonOpts);
            User = loaded is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, RenameOptions>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A corrupt preset file must never block the renamer.
            User = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(User, JsonOpts));
    }

    public bool IsBuiltIn(string name) => BuiltIn.ContainsKey(name);

    public RenameOptions? Get(string name) =>
        BuiltIn.TryGetValue(name, out var b) ? b.Clone()
        : User.TryGetValue(name, out var u) ? u.Clone()
        : null;
}
