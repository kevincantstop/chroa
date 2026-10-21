using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ChroaVideoPlayer.Core.Plugin.Context;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public partial class SubtitleMovementFrame : UserControl
{
    private const double FrameWidthRatio = 0.6;

    private const double FrameHeightRatio = 0.12;

    private const double FrameMinHeight = 32;

    private const double ConfirmSize = 36;

    private const double ConfirmInset = 8;

    private SubtitleWindowViewModel? _viewModel;

    private IVideoOverlayHost? _overlay;

    private IDisposable? _registration;

    private double _aspect = 16.0 / 9.0;

    private double _xPercent = 50;

    private double _yPercent = 88;

    private Point _dragOrigin;

    private double _dragX;

    private double _dragY;

    private bool _dragging;

    public SubtitleMovementFrame()
    {
        InitializeComponent();

        Frame.PointerPressed += OnFramePressed;
        Frame.PointerMoved += OnFrameMoved;
        Frame.PointerReleased += OnFrameReleased;
    }

    public void Attach(SubtitleWindowViewModel viewModel, IVideoOverlayHost overlay)
    {
        Detach();

        _viewModel = viewModel;
        _overlay = overlay;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Apply();
    }

    public void Detach()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = null;

        Close();

        _overlay = null;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        ArrangeFrame(finalSize);

        return size;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply()
    {
        if (_viewModel is not { } viewModel) return;

        _aspect = double.IsFinite(viewModel.VideoAspect) && viewModel.VideoAspect > 0
            ? viewModel.VideoAspect
            : 16.0 / 9.0;

        var (x, y) = viewModel.EditedSourcePosition;

        _xPercent = Math.Clamp(x, 0, 100);
        _yPercent = Math.Clamp(y, 0, 100);

        ArrangeFrame(Bounds.Size);

        if (viewModel.IsFrameVisible) Open();
        else Close();
    }

    private void Open()
    {
        if (_registration is not null) return;
        if (_overlay is not { } overlay) return;

        _registration = overlay.Add(this);

        _viewModel?.DisableControls();
    }

    private void Close()
    {
        _registration?.Dispose();

        _registration = null;

        _viewModel?.EnableControls();
    }

    private void ArrangeFrame(Size size)
    {
        var picture = PictureRect(size);

        if (picture.Width <= 0 || picture.Height <= 0) return;

        var frameWidth = Math.Max(80, picture.Width * FrameWidthRatio);
        var frameHeight = Math.Max(FrameMinHeight, picture.Height * FrameHeightRatio);

        Frame.Width = frameWidth;
        Frame.Height = frameHeight;

        var centerX = picture.X + picture.Width * _xPercent / 100;
        var centerY = picture.Y + picture.Height * _yPercent / 100;

        var left = Math.Clamp(centerX - frameWidth / 2, picture.X, picture.X + picture.Width - frameWidth);
        var top = Math.Clamp(centerY - frameHeight / 2, picture.Y, picture.Y + picture.Height - frameHeight);

        Canvas.SetLeft(Frame, left);
        Canvas.SetTop(Frame, top);

        Canvas.SetLeft(ConfirmButton, left + frameWidth - ConfirmSize - ConfirmInset);
        Canvas.SetTop(ConfirmButton, top + (frameHeight - ConfirmSize) / 2);
    }

    private Rect PictureRect(Size size)
    {
        var width = size.Width;
        var height = size.Height;

        if (width <= 0 || height <= 0) return default;

        if (width / height > _aspect)
        {
            var pictureWidth = height * _aspect;

            return new Rect((width - pictureWidth) / 2, 0, pictureWidth, height);
        }

        var pictureHeight = width / _aspect;

        return new Rect(0, (height - pictureHeight) / 2, width, pictureHeight);
    }

    private void OnFramePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Frame).Properties.IsLeftButtonPressed) return;

        e.Handled = true;

        _dragging = true;
        _dragOrigin = e.GetPosition(this);
        _dragX = _xPercent;
        _dragY = _yPercent;

        e.Pointer.Capture(Frame);
    }

    private void OnFrameMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _viewModel is not { } viewModel) return;

        e.Handled = true;

        var picture = PictureRect(Bounds.Size);

        if (picture.Width <= 0 || picture.Height <= 0) return;

        var position = e.GetPosition(this);

        _xPercent = Math.Clamp(_dragX + (position.X - _dragOrigin.X) / picture.Width * 100, 0, 100);
        _yPercent = Math.Clamp(_dragY + (position.Y - _dragOrigin.Y) / picture.Height * 100, 0, 100);

        ArrangeFrame(Bounds.Size);

        viewModel.MoveEditedSource(_xPercent, _yPercent);
    }

    private void OnFrameReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;

        e.Handled = true;

        _dragging = false;

        e.Pointer.Capture(null);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_viewModel is { } viewModel) viewModel.IsFrameVisible = false;
    }
}
