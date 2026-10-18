using Avalonia.Controls;

namespace ChroaVideoPlayer.Core.UI;

public class AlertDialog : ChroaDialog
{
    public AlertDialog(string title, string message, string okText = "OK") : base(title, message)
    {
        DefaultAction = Close;

        AddAction(okText, true, Close);
    }
}
