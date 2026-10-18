using System;

namespace ChroaVideoPlayer.Core.EventBus.Events;

public sealed record VideoProgress(string Path, TimeSpan Position, TimeSpan Duration);
