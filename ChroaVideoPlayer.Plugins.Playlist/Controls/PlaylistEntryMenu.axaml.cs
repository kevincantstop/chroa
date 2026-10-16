using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ChroaVideoPlayer.Plugins.Playlist;

public partial class PlaylistEntryMenu : ContextMenu
{
    protected override Type StyleKeyOverride => typeof(ContextMenu);

    public PlaylistEntryMenu()
    {
        InitializeComponent();
    }

    public event Action<PlaylistEntry>? PlayRequested;

    public event Action<PlaylistEntry>? DeleteRequested;

    public PlaylistEntry? Entry { get; set; }

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
}
