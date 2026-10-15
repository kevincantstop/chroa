using ChroaVideoPlayer.Core.Plugin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChroaVideoPlayer.ViewModels;

public partial class PluginItem : ObservableObject
{
    public PluginItem(IChroaPlugin plugin) => Plugin = plugin;

    public IChroaPlugin Plugin { get; }

    public string Label => Plugin.Label;

    public string Icon => Plugin.Icon;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }
}
