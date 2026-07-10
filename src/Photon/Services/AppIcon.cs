namespace Photon.App.Services;

/// <summary>
/// The application icon, extracted once from the exe (embedded via ApplicationIcon in the
/// csproj). Falls back silently when running unembedded (e.g. dev builds without the asset).
/// </summary>
public static class AppIcon
{
    private static readonly Lazy<Icon?> Cached = new(() =>
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    });

    public static void Apply(Form form)
    {
        if (Cached.Value is { } icon) form.Icon = icon;
    }
}
