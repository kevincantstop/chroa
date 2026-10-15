using System;
using System.ComponentModel;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistEntry : INotifyPropertyChanged
{
    private bool _isPlaying;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; set; } = string.Empty;

    public TimeSpan Duration { get; set; }

    public DateTime LastPlayedUtc { get; set; }

    public bool IsPlaying => _isPlaying;

    public string Title => System.IO.Path.GetFileName(Path);

    public string DurationText => Duration >= TimeSpan.FromHours(1)
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    public void MarkPlaying(bool playing)
    {
        if (_isPlaying == playing) return;

        _isPlaying = playing;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
    }
}
