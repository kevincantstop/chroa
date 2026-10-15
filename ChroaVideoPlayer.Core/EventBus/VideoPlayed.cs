using System;

namespace ChroaVideoPlayer.Core.EventBus;

public sealed record VideoPlayed(string Path, TimeSpan Duration);
