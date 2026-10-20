using System.Threading.Tasks;
using Avalonia.Controls;
using ChroaVideoPlayer.Core.UI;

namespace ChroaVideoPlayer.Core.Utils;

public static class ChroaDialogs
{
    public static Task AlertAsync(Window owner, string message, string title = "Warning", string okText = "OK") =>
        new AlertDialog(title, message, okText).ShowDialog(owner);

    public static Task<bool> PromptAsync(
        Window owner,
        string message,
        string title = "Confirm",
        string okText = "OK",
        string cancelText = "Cancel") =>
        new PromptDialog(title, message, okText, cancelText).ShowDialog<bool>(owner);

    public static Task<string?> InputAsync(
        Window owner,
        string message,
        string title = "Input",
        string value = "",
        string okText = "OK",
        string cancelText = "Cancel") =>
        new InputDialog(title, message, value, okText, cancelText).ShowDialog<string?>(owner);
}
