using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChroaVideoPlayer.Core.Mpv;

namespace ChroaVideoPlayer.Controls;

public partial class LoadVideoButton : UserControl
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<LoadVideoButton, string?>(nameof(Icon), "/Icons/film.svg");

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LoadVideoButton, string?>(nameof(Text), "Load Video");

    public static readonly StyledProperty<ChroaMpvContext?> PlayerProperty =
        AvaloniaProperty.Register<LoadVideoButton, ChroaMpvContext?>(nameof(Player));

    private static readonly FilePickerFileType VideoFileType = new("Video Files")
    {
        Patterns = new[]
        {
            "*.mp4", "*.mkv", "*.mov", "*.m4v", "*.avi", "*.webm", "*.flv",
            "*.wmv", "*.ts", "*.m2ts", "*.mpg", "*.mpeg", "*.rmvb", "*.3gp",
        },
        MimeTypes = new[] { "video/*" },
        AppleUniformTypeIdentifiers = new[] { "public.movie" },
    };

    private static readonly FilePickerFileType AllFileType = new("All Files")
    {
        Patterns = new[] { "*.*" },
    };

    public LoadVideoButton()
    {
        InitializeComponent();
    }

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ChroaMpvContext? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty)
        {
            IconElement.Icon = Icon;
        }
        else if (change.Property == TextProperty)
        {
            LabelElement.Text = Text;
        }
    }

    private async void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Text,
                AllowMultiple = false,
                FileTypeFilter = [VideoFileType, AllFileType],
            });

            if (files.Count == 0) return;

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            Player?.Play(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open file picker failed: {ex}");
        }
    }
}