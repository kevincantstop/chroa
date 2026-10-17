using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ChroaVideoPlayer.Core.UI;

public class ChroaWindow : Window
{
    public const double ShadowMargin = 24;

    public const double PanelCornerRadius = 10;

    private static readonly Thickness PanelMargin = new(ShadowMargin);

    private static readonly CornerRadius PanelCorners = new(PanelCornerRadius);

    private static readonly BoxShadows PanelShadow = BoxShadows.Parse("0 6 24 0 #A0000000");

    private readonly Border _panel;

    private readonly Border _clip;

    private bool _isPanelFlush;

    public ChroaWindow()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;

        _clip = new Border
        {
            CornerRadius = PanelCorners,
            ClipToBounds = true,
        };

        _panel = new Border
        {
            Margin = PanelMargin,
            Background = Brushes.Black,
            CornerRadius = PanelCorners,
            BoxShadow = PanelShadow,
            Child = _clip,
        };

        base.Content = _panel;
    }

    public Control? PanelContent
    {
        get => _clip.Child;
        set => _clip.Child = value;
    }

    protected Border PanelBorder => _panel;

    public bool IsPanelFlush
    {
        get => _isPanelFlush;
        set
        {
            if (_isPanelFlush == value) return;

            _isPanelFlush = value;

            _panel.Margin = value ? default : PanelMargin;
            _panel.CornerRadius = value ? default : PanelCorners;
            _panel.BoxShadow = value ? default : PanelShadow;
            _clip.CornerRadius = _panel.CornerRadius;
        }
    }
}
