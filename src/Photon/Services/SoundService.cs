using System.Media;

namespace Photon.App.Services;

/// <summary>Plays the "done" sound: the user's WAV when set and readable, else a system beep.</summary>
public sealed class SoundService : IDisposable
{
    // Kept alive so async playback isn't cut off by disposal.
    private SoundPlayer? _player;

    public void PlayDone(string? wavPath)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(wavPath) && File.Exists(wavPath))
            {
                _player?.Dispose();
                _player = new SoundPlayer(wavPath);
                _player.Play();
                return;
            }
        }
        catch
        {
            // Bad/corrupt WAV: fall through to the beep.
        }
        try { SystemSounds.Beep.Play(); } catch { }
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) _player?.Dispose();
        _player = null;
    }
}
