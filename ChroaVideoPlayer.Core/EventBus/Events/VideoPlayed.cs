using System;

namespace ChroaVideoPlayer.Core.EventBus.Events;

public sealed record VideoPlayed(string Path, TimeSpan Duration);
