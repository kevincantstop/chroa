using System;
using Avalonia.Controls;
using ChroaVideoPlayer.Core.Plugin.Context;

namespace ChroaVideoPlayer.Core.Plugin.UI;

public sealed class VideoOverlayHost : IVideoOverlayHost
{
    private readonly Panel _root;

    private readonly Control? _anchor;

    public VideoOverlayHost(Panel root, Control? anchor = null)
    {
        _root = root;
        _anchor = anchor;
    }

    public IDisposable Add(Control overlay)
    {
        var index = _anchor is null ? _root.Children.Count : _root.Children.IndexOf(_anchor) + 1;

        _root.Children.Insert(Math.Clamp(index, 0, _root.Children.Count), overlay);

        return new Registration(_root, overlay);
    }

    private sealed class Registration : IDisposable
    {
        private readonly Panel _root;

        private readonly Control _overlay;

        public Registration(Panel root, Control overlay)
        {
            _root = root;
            _overlay = overlay;
        }

        public void Dispose() => _root.Children.Remove(_overlay);
    }
}
