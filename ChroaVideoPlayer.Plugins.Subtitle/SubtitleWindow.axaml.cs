using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Plugin.UI;
using ChroaVideoPlayer.Core.Utils;

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

    private async void OnAddSubtitleClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Subtitle",
                AllowMultiple = true,
                FileTypeFilter = SubtitleFileTypes.Filter,
            });

            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();

                if (!string.IsNullOrEmpty(path)) ViewModel?.LoadSubtitle(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Add subtitle failed: {ex}");
        }
    }
}
