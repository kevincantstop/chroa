using System.Collections.Generic;

namespace ChroaVideoPlayer.Core.ClipCapture;

public sealed record ClipCaptureResult(IReadOnlyList<string> Frames, int FramesPerSecond);
