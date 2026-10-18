using System;
using System.ComponentModel;
using ChroaVideoPlayer.Core.Persistance.Attributes;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistEntry : INotifyPropertyChanged
{
    private bool _isPlaying;

    private bool _isSelected;

    private bool _isCurrent;

    private bool _isMissing;

    private bool _canMoveUp;

    private bool _canMoveDown;

    public event PropertyChangedEventHandler? PropertyChanged;

    [PersistentColumn]
    public string Path { get; set; } = string.Empty;

    [PersistentColumn]
    public TimeSpan Duration { get; set; }

    [PersistentColumn]
    public DateTime LastPlayedUtc { get; set; }

    [PersistentColumn]
    public DateTime AddedUtc { get; set; }

    [PersistentColumn]
    public int SortIndex { get; set; }

    public bool IsPlaying => _isPlaying;

    public bool IsSelected => _isSelected;

    public bool IsCurrent => _isCurrent;

    public bool IsMissing => _isMissing;

    public bool CanMoveUp => _canMoveUp;

    public bool CanMoveDown => _canMoveDown;

    public string RowTip => _isMissing ? "File not found" : "Double click to play";

    public string Title => System.IO.Path.GetFileName(Path);

    public string DurationText => Duration <= TimeSpan.Zero
        ? "--:--"
        : Duration >= TimeSpan.FromHours(1)
            ? Duration.ToString(@"h\:mm\:ss")
            : Duration.ToString(@"m\:ss");

    public void MarkPlaying(bool playing) => SetFlag(ref _isPlaying, playing, nameof(IsPlaying));

    public void MarkSelected(bool selected) => SetFlag(ref _isSelected, selected, nameof(IsSelected));

    public void MarkCurrent(bool current) => SetFlag(ref _isCurrent, current, nameof(IsCurrent));

    public void MarkMovable(bool up, bool down)
    {
        SetFlag(ref _canMoveUp, up, nameof(CanMoveUp));
        SetFlag(ref _canMoveDown, down, nameof(CanMoveDown));
    }

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
