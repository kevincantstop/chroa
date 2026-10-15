using System.Collections.ObjectModel;
using System.Linq;
using ChroaVideoPlayer.Core.EventBus;
using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistPlugin : IChroaPlugin, IChroaPluginEvents
{
    private readonly ObservableCollection<PlaylistEntry> _entries = new();

    public string Name => "playlist";

    public string Label => "Playlist";

    public string Icon => "avares://ChroaVideoPlayer.Plugins.Playlist/Icons/playlist.svg";

    public PluginLocation Location => PluginLocation.MainMenu;

    public void SubscribeEvents(IEventSubscriber events) => events.Subscribe<VideoPlayed>(OnVideoPlayed);

    public PluginWindow CreateWindow()
    {
        var window = new PlaylistPluginWindow();

        window.ShowEntries(_entries);

        return window;
    }

    private void OnVideoPlayed(VideoPlayed message)
    {
        var existing = _entries.FirstOrDefault(entry => entry.Path == message.Path);

        if (existing is not null) _entries.Remove(existing);

        _entries.Insert(0, new PlaylistEntry(message.Path, message.Duration));
    }
}
