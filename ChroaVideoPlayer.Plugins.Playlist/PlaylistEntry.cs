using System;
using System.ComponentModel;
using ChroaVideoPlayer.Core.Persistance.Attributes;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistEntry : INotifyPropertyChanged
{
    private const double CompletionToleranceSeconds = 1;

    private bool _isPlaying;

    private bool _isSelected;

    private bool _isCurrent;

    private bool _isMissing;

    private bool _canMoveUp;

    private bool _canMoveDown;

    private bool _isDropBefore;

    private bool _isDropAfter;

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

    [PersistentColumn]
    public double PlayedSeconds { get; set; }

    public bool IsPlaying => _isPlaying;

    public bool IsSelected => _isSelected;

    public bool IsCurrent => _isCurrent;

    public bool IsMissing => _isMissing;

    public bool CanMoveUp => _canMoveUp;

    public bool CanMoveDown => _canMoveDown;

    public bool IsDropBefore => _isDropBefore;

    public bool IsDropAfter => _isDropAfter;

    public string RowTip => _isMissing ? "File not found" : "Double click to play";

    public string Title => System.IO.Path.GetFileName(Path);

    public string DurationText => Duration <= TimeSpan.Zero
        ? "--:--"
        : Duration >= TimeSpan.FromHours(1)
            ? Duration.ToString(@"h\:mm\:ss")
            : Duration.ToString(@"m\:ss");

    public string PlayedDurationText => PlayedSeconds <= 0
        ? "--:--"
        : Format(TimeSpan.FromSeconds(PlayedSeconds));

    public string PlayedPercentText => PlayedFraction is { } fraction
        ? $"{Math.Round(fraction * 100)}%"
        : "--";

    public bool IsCompleted => PlayedFraction >= 0.995;

    public TimeSpan ResumePosition => Duration > TimeSpan.Zero &&
                                      PlayedSeconds > 0 &&
                                      Duration.TotalSeconds - PlayedSeconds > CompletionToleranceSeconds
        ? TimeSpan.FromSeconds(PlayedSeconds)
        : TimeSpan.Zero;

    public void MarkPlaying(bool playing) => SetFlag(ref _isPlaying, playing, nameof(IsPlaying));

    public void MarkSelected(bool selected) => SetFlag(ref _isSelected, selected, nameof(IsSelected));

    public void MarkCurrent(bool current) => SetFlag(ref _isCurrent, current, nameof(IsCurrent));

    public void MarkMovable(bool up, bool down)
    {
        SetFlag(ref _canMoveUp, up, nameof(CanMoveUp));
        SetFlag(ref _canMoveDown, down, nameof(CanMoveDown));
    }

    public void MarkDrop(bool before, bool after)
    {
        SetFlag(ref _isDropBefore, before, nameof(IsDropBefore));
        SetFlag(ref _isDropAfter, after, nameof(IsDropAfter));
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

    public void MarkPlayed(double seconds)
    {
        if (Math.Abs(PlayedSeconds - seconds) < 1) return;

        PlayedSeconds = Math.Max(0, seconds);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayedDurationText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayedPercentText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompleted)));
    }

    private double? PlayedFraction
    {
        get
        {
            if (Duration <= TimeSpan.Zero || PlayedSeconds <= 0) return null;

            if (Duration.TotalSeconds - PlayedSeconds <= CompletionToleranceSeconds) return 1;

            return Math.Clamp(PlayedSeconds / Duration.TotalSeconds, 0, 1);
        }
    }

    private static string Format(TimeSpan value) => value >= TimeSpan.FromHours(1)
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");

    private void SetFlag(ref bool field, bool value, string name)
    {
        if (field == value) return;

        field = value;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
