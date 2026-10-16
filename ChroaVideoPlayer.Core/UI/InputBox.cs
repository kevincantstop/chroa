using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ChroaVideoPlayer.Core.UI;

public class InputBox : TextBox
{
    private TopLevel? _owner;

    private IInputElement? _blurTarget;

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _owner = TopLevel.GetTopLevel(this);
        _blurTarget = ResolveBlurTarget(_owner);

        _owner?.AddHandler(PointerPressedEvent, OnOwnerPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _owner?.RemoveHandler(PointerPressedEvent, OnOwnerPointerPressed);

        _owner = null;
        _blurTarget = null;

        base.OnDetachedFromVisualTree(e);
    }

    private static IInputElement? ResolveBlurTarget(TopLevel? owner)
    {
        if (owner?.Content is not Control root) return null;

        root.Focusable = true;
        root.IsTabStop = false;

        return root;
    }

    private void OnOwnerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_blurTarget is null || !IsFocused) return;
        if (e.Source is Visual source && (ReferenceEquals(source, this) || this.IsVisualAncestorOf(source))) return;

        _blurTarget.Focus();
    }
}
