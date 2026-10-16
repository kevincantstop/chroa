using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ChroaVideoPlayer.Plugins.Playlist;

public partial class PlaylistEntryMenu : ContextMenu
{
    private PlaylistEntry? _entry;

    protected override Type StyleKeyOverride => typeof(ContextMenu);

    public PlaylistEntryMenu()
    {
        InitializeComponent();
    }

    public event Action<PlaylistEntry>? PlayRequested;

    public event Action<PlaylistEntry>? DeleteRequested;

    public event Action<PlaylistEntry>? RevealRequested;

    public PlaylistEntry? Entry
    {
        get => _entry;
        set
        {
            _entry = value;

            var missing = value?.IsMissing ?? false;

            PlayItem.IsEnabled = !missing;
            RevealItem.IsEnabled = !missing;
        }
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (Entry is not { } entry) return;

        PlayRequested?.Invoke(entry);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (Entry is not { } entry) return;

        DeleteRequested?.Invoke(entry);
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        if (Entry is not { } entry) return;

        RevealRequested?.Invoke(entry);
    }
}
