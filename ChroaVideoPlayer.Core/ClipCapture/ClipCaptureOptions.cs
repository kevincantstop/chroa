using System;

namespace ChroaVideoPlayer.Core.ClipCapture;

public sealed record ClipCaptureOptions
{
    public string? HeadlessPath { get; init; }

    public int FramesPerSecond { get; init; } = 12;

    public int FrameWidth { get; init; } = 320;

    public int MaxConcurrency { get; init; } = 2;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
