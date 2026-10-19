using Avalonia;
using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleWindow : PluginWindow
{
    public SubtitleWindow()
    {
        InitializeComponent();

        DataContext = new SubtitleWindowViewModel();
    }

    private SubtitleWindowViewModel? ViewModel => DataContext as SubtitleWindowViewModel;

    public void Attach(ChroaMpvContext mpv) => ViewModel?.Attach(mpv);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        ViewModel?.Detach();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
