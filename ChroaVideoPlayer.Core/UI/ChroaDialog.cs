using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChroaVideoPlayer.Core.UI;

public abstract class ChroaDialog : ChroaWindow
{
    private static readonly IBrush TextBrush = CreateBrush("#C8C8C8");

    private static readonly IBrush PrimaryBackground = CreateBrush("#E0E0E0");

    private static readonly IBrush PrimaryBackgroundHover = CreateBrush("#FFFFFF");

    private static readonly IBrush SecondaryBackground = CreateBrush("#1AFFFFFF");

    private static readonly IBrush SecondaryBackgroundHover = CreateBrush("#33FFFFFF");

    private static readonly IBrush PrimaryForeground = CreateBrush("#101010");

    private static readonly IBrush SecondaryForeground = CreateBrush("#E0E0E0");

    private readonly StackPanel _actions;

    protected ChroaDialog(string title, string message)
    {
        ShowInTaskbar = false;
        CanMinimize = false;
        CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        Width = 380;

        PanelBorder.BorderBrush = CreateBrush("#FFFFFF");
        PanelBorder.BorderThickness = new Thickness(1);

        _actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 16,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        };

        var body = new TextBlock
        {
            Text = message,
            FontSize = 14,
            Foreground = TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };

        PanelContent = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Children =
            {
                heading,
                body,
                _actions,
            },
        };
    }

    protected Action? DefaultAction { get; set; }

    protected Action? CancelAction { get; set; }

    protected void AddAction(string text, bool primary, Action action) =>
        _actions.Children.Add(CreateButton(text, primary, action));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelAction?.Invoke();

            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            DefaultAction?.Invoke();

            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private static Control CreateButton(string text, bool primary, Action action)
    {
        var normal = primary ? PrimaryBackground : SecondaryBackground;
        var hover = primary ? PrimaryBackgroundHover : SecondaryBackgroundHover;

        var button = new Border
        {
            Background = normal,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(18, 7),
            MinWidth = 84,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 14,
                Foreground = primary ? PrimaryForeground : SecondaryForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        button.PointerEntered += (_, _) => button.Background = hover;
        button.PointerExited += (_, _) => button.Background = normal;
        button.Tapped += (_, e) =>
        {
            e.Handled = true;

            action();
        };

        return button;
    }

    private static IBrush CreateBrush(string value) => new SolidColorBrush(Color.Parse(value));
}
