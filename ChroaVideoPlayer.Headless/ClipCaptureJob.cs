using System;
using System.Globalization;
using System.IO;
using System.Threading;
using ChroaVideoPlayer.Core.ClipCapture;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;

namespace ChroaVideoPlayer.Headless;

internal sealed class ClipCaptureJob
{
    public string? Run(CaptureRequest request)
    {
        if (!File.Exists(request.Input)) return $"Input file not found: {request.Input}";

        Directory.CreateDirectory(request.OutputDirectory);

        Clear(request.OutputDirectory);

        MpvApi.RootPath = Path.Combine(AppContext.BaseDirectory, "native");

        using var context = new MpvContext();

        using var finished = new ManualResetEventSlim(false);

        context.EndFile += (_, _) => finished.Set();

        Apply(context, request);

        context.LoadFile(request.Input).InvokeAsync().GetAwaiter().GetResult();

        finished.Wait();

        if (Rename(request.OutputDirectory) == 0) return "No frames were produced.";

        File.WriteAllText(
            Path.Combine(request.OutputDirectory, ClipCaptureProtocol.CompletionMarker), string.Empty);

        return null;
    }

    private static void Apply(MpvContext context, CaptureRequest request)
    {
        Set(context, "vo", "image");
        Set(context, "vo-image-outdir", request.OutputDirectory);
        Set(context, "vo-image-format", "png");
        Set(context, "audio", "no");
        Set(context, "sub", "no");
        Set(context, "keep-open", "no");
        Set(context, "untimed", "yes");
        Set(context, "msg-level", "all=error");
        Set(context, "start", Seconds(request.CaptureAt));

        if (request.Duration > TimeSpan.Zero)
        {
            Set(context, "length", Seconds(request.Duration));
            Set(context, "vf", $"fps={request.FramesPerSecond},scale={request.Width}:-2");
        }
        else
        {
            Set(context, "frames", "1");
            Set(context, "vf", $"scale={request.Width}:-2");
        }
    }

    private static void Clear(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*.png")) File.Delete(file);

        var marker = Path.Combine(directory, ClipCaptureProtocol.CompletionMarker);

        if (File.Exists(marker)) File.Delete(marker);
    }

    private static int Rename(string directory)
    {
        var files = Directory.GetFiles(directory, "*.png");

        Array.Sort(files, StringComparer.Ordinal);

        for (var index = 0; index < files.Length; index++)
            File.Move(
                files[index],
                Path.Combine(directory, $"{ClipCaptureProtocol.FramePrefix}{index + 1:0000}.png"));

        return files.Length;
    }

    private static void Set(MpvContext context, string name, string value)
    {
        try
        {
            context.Option[name].Set(value);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Option {name}={value} was rejected: {ex.Message}");
        }
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
