using Avalonia.Controls;

namespace ChroaVideoPlayer.Core.UI;

public class PromptDialog : ChroaDialog
{
    public PromptDialog(string title, string message, string okText = "OK", string cancelText = "Cancel")
        : base(title, message)
    {
        DefaultAction = Accept;
        CancelAction = () => Close(false);

        AddAction(cancelText, false, () => Close(false));
        AddAction(okText, true, Accept);
    }

    private void Accept() => Close(true);
}
