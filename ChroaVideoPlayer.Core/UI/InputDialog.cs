using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ChroaVideoPlayer.Core.UI;

public class InputDialog : ChroaDialog
{
    private readonly TextBox _input;

    public InputDialog(
        string title,
        string message,
        string value = "",
        string okText = "OK",
        string cancelText = "Cancel")
        : base(title, message)
    {
        _input = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(10, 6),
            Background = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Foreground = new SolidColorBrush(Color.Parse("#E0E0E0")),
            CaretBrush = new SolidColorBrush(Color.Parse("#E0E0E0")),
            SelectionBrush = new SolidColorBrush(Color.Parse("#40FFFFFF")),
            FontSize = 14,
        };

        AddField(_input);

        DefaultAction = Accept;
        CancelAction = () => Close(null);

        AddAction(cancelText, false, () => Close(null));
        AddAction(okText, true, Accept);

        Opened += (_, _) => _input.Focus();
    }

    private void Accept() => Close(_input.Text?.Trim() ?? string.Empty);
}
