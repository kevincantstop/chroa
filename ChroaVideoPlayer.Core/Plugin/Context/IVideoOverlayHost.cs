using System;
using Avalonia.Controls;

namespace ChroaVideoPlayer.Core.Plugin.Context;

public interface IVideoOverlayHost
{
    IDisposable Add(Control overlay);
}
