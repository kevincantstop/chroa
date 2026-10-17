using System;
using System.Globalization;
using ChroaVideoPlayer.Core.ClipCapture;

namespace ChroaVideoPlayer.Headless;

internal sealed record CaptureRequest(
    string Input,
    TimeSpan CaptureAt,
    TimeSpan Duration,
    int FramesPerSecond,
    int Width,
    string OutputDirectory)
{
    public static bool TryParse(string[] args, out CaptureRequest request, out string error)
    {
        request = new CaptureRequest(string.Empty, TimeSpan.Zero, TimeSpan.Zero, 0, 0, string.Empty);
        error = string.Empty;

        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            var name = args[index];
            var value = args[index + 1];

            switch (name)
            {
                case ClipCaptureProtocol.InputArgument:
                    request = request with { Input = value };
                    break;
                case ClipCaptureProtocol.OutputArgument:
                    request = request with { OutputDirectory = value };
                    break;
                case ClipCaptureProtocol.CaptureAtArgument:
                    if (!TrySeconds(value, out var captureAt)) return Fail($"Invalid {name}: {value}", out error);
                    request = request with { CaptureAt = captureAt };
                    break;
                case ClipCaptureProtocol.DurationArgument:
                    if (!TrySeconds(value, out var duration)) return Fail($"Invalid {name}: {value}", out error);
                    request = request with { Duration = duration };
                    break;
                case ClipCaptureProtocol.FramesPerSecondArgument:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var framesPerSecond))
                        return Fail($"Invalid {name}: {value}", out error);
                    request = request with { FramesPerSecond = framesPerSecond };
                    break;
                case ClipCaptureProtocol.WidthArgument:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
                        return Fail($"Invalid {name}: {value}", out error);
                    request = request with { Width = width };
                    break;
                default:
                    return Fail($"Unknown argument: {name}", out error);
            }
        }

        if (request.Input.Length == 0) return Fail($"Missing {ClipCaptureProtocol.InputArgument}", out error);
        if (request.OutputDirectory.Length == 0) return Fail($"Missing {ClipCaptureProtocol.OutputArgument}", out error);
        if (request.CaptureAt < TimeSpan.Zero) return Fail("Capture position must not be negative.", out error);
        if (request.Duration < TimeSpan.Zero) return Fail("Duration must not be negative.", out error);
        if (request.Width <= 0) return Fail("Frame width must be positive.", out error);

        if (request.Duration > TimeSpan.Zero && request.FramesPerSecond <= 0)
            return Fail("Frame rate must be positive for a clip.", out error);

        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;

        return false;
    }

    private static bool TrySeconds(string value, out TimeSpan result)
    {
        result = TimeSpan.Zero;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return false;

        result = TimeSpan.FromSeconds(seconds);

        return true;
    }
}
