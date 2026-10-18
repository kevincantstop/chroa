using System;
using ChroaVideoPlayer.Core.EventBus;
using ChroaVideoPlayer.Core.EventBus.Events;
using ChroaVideoPlayer.Core.Plugin;
using ChroaVideoPlayer.Core.Plugin.Context;
using ChroaVideoPlayer.Core.Plugin.UI;
using ChroaVideoPlayer.Plugins.Playlist.Service;

namespace ChroaVideoPlayer.Plugins.Playlist;

public sealed class PlaylistPlugin : IChroaPlugin, IChroaPluginEvents
{
    private PlaylistService? _service;

    public string Name => "playlist";

    public string Label => "Playlist";

    public string Icon => "avares://ChroaVideoPlayer.Plugins.Playlist/Icons/playlist.svg";

    public PluginLocation Location => PluginLocation.MainMenu;

    public bool Enabled => true;

    public ChroaApplicationContext? Context { get; set; }

    public PlaylistService Service => _service ??= CreateService();

    public void SubscribeEvents(IEventSubscriber events)
    {
        events.Subscribe<VideoPlayed>(OnVideoPlayed);

        events.Subscribe<VideoEnded>(OnVideoEnded);

        events.Subscribe<VideoStepRequested>(OnVideoStepRequested);

        events.Subscribe<VideoProgress>(OnVideoProgress);
    }

    public PluginWindow CreateWindow()
    {
        var window = new PlaylistPluginWindow();

        window.ShowService(Service);

        return window;
    }

    private PlaylistService CreateService()
    {
        var service = new PlaylistService();

        service.PlaybackRequested += OnPlaybackRequested;

        return service;
    }

    private void OnPlaybackRequested(string path) => Context?.Mpv.Play(path);

    private void OnVideoPlayed(VideoPlayed message) => Service.HandleVideoPlayed(message.Path, message.Duration);

    private void OnVideoEnded(VideoEnded message) => Service.HandleVideoEnded(message.Path);

    private void OnVideoStepRequested(VideoStepRequested message) => Service.Step(message.Offset);

    private void OnVideoProgress(VideoProgress message) =>
        Service.HandleVideoProgress(message.Path, message.Position, message.Duration);
}
