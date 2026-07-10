using Photon.App.Services;

namespace Photon.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var settingsService = new SettingsService();
        settingsService.Load();

        ApplicationConfiguration.Initialize();
        // SetColorMode only takes effect before the first window is created.
        ThemeService.ApplyColorMode(settingsService.Current.Theme);

        Application.Run(new MainForm(settingsService));
    }
}
