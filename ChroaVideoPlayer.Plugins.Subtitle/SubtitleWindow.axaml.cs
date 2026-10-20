using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChroaVideoPlayer.Core.Mpv;
using ChroaVideoPlayer.Core.Plugin.Context;
using ChroaVideoPlayer.Core.Plugin.UI;
using ChroaVideoPlayer.Core.Utils;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleWindow : PluginWindow
{
    private readonly SubtitleMovementFrame _frame = new();

    public SubtitleWindow()
    {
        InitializeComponent();

        DataContext = new SubtitleWindowViewModel();
    }

    private SubtitleWindowViewModel? ViewModel => DataContext as SubtitleWindowViewModel;

    public void Attach(ChroaMpvContext mpv, IVideoOverlayHost? overlay, IVideoControls? controls)
    {
        if (ViewModel is not { } viewModel) return;

        viewModel.Attach(mpv, overlay, controls);

        if (overlay is not null) _frame.Attach(viewModel, overlay);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        ViewModel?.Detach();

        _frame.Detach();
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
