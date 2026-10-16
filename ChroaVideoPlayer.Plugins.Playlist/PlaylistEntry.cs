using System;
using System.ComponentModel;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistEntry : INotifyPropertyChanged
{
    private bool _isPlaying;

    private bool _isSelected;

    private bool _isMissing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; set; } = string.Empty;

    public TimeSpan Duration { get; set; }

    public DateTime LastPlayedUtc { get; set; }

    public DateTime AddedUtc { get; set; }

    public bool IsPlaying => _isPlaying;

    public bool IsSelected => _isSelected;

    public bool IsMissing => _isMissing;

    public string RowTip => _isMissing ? "File not found" : "Double click to play";

    public string Title => System.IO.Path.GetFileName(Path);

    public string DurationText => Duration <= TimeSpan.Zero
        ? "--:--"
        : Duration >= TimeSpan.FromHours(1)
            ? Duration.ToString(@"h\:mm\:ss")
            : Duration.ToString(@"m\:ss");

    public void MarkPlaying(bool playing) => SetFlag(ref _isPlaying, playing, nameof(IsPlaying));

    public void MarkSelected(bool selected) => SetFlag(ref _isSelected, selected, nameof(IsSelected));

    public void MarkMissing(bool missing)
    {
        if (_isMissing == missing) return;

        _isMissing = missing;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMissing)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowTip)));
    }

    public void MarkDuration(TimeSpan duration)
    {
        if (Duration == duration) return;

        Duration = duration;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationText)));
    }

    private void SetFlag(ref bool field, bool value, string name)
    {
        if (field == value) return;

        field = value;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
