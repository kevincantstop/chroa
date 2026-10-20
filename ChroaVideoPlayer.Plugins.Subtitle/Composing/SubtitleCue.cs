using System;

namespace ChroaVideoPlayer.Plugins.Subtitle;

public readonly record struct SubtitleCue(TimeSpan Start, TimeSpan End, string Text);
