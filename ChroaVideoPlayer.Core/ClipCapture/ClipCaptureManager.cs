using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChroaVideoPlayer.Core.ClipCapture;

public sealed class ClipCaptureManager
{
    private const string HeadlessExecutable = "ChroaVideoPlayer.Headless.exe";

    private readonly TaskRunner _runner;

    private readonly SemaphoreSlim _gate;

    public ClipCaptureManager(ClipCaptureOptions? options = null)
    {
        Options = options ?? new ClipCaptureOptions();

        _runner = new TaskRunner(Options.Timeout);
        _gate = new SemaphoreSlim(Math.Max(1, Options.MaxConcurrency));
    }

    public ClipCaptureOptions Options { get; }

    public static string DefaultHeadlessPath =>
        Path.Combine(AppContext.BaseDirectory, HeadlessExecutable);

    public static string PreviewRoot =>
        Path.Combine(Path.GetTempPath(), "ChroaVideoPlayer", "previews");

    public static void ClearCache()
    {
        try
        {
            if (Directory.Exists(PreviewRoot)) Directory.Delete(PreviewRoot, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not clear the clip preview cache: {ex.Message}");
        }
    }

    public async Task<ClipCaptureResult?> CaptureAsync(
        string path,
        TimeSpan captureAt,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;

        if (captureAt < TimeSpan.Zero) captureAt = TimeSpan.Zero;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

        var framesPerSecond = duration > TimeSpan.Zero ? Math.Max(1, Options.FramesPerSecond) : 0;
        var directory = CacheDirectory(path, captureAt, duration, framesPerSecond);

        var frames = ReadFrames(directory);

        if (frames.Count > 0) return new ClipCaptureResult(frames, framesPerSecond);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            frames = ReadFrames(directory);

            if (frames.Count > 0) return new ClipCaptureResult(frames, framesPerSecond);

            Directory.CreateDirectory(directory);

            var result = await _runner.RunAsync(
                Options.HeadlessPath ?? DefaultHeadlessPath,
                Arguments(path, captureAt, duration, framesPerSecond, directory),
                cancellationToken).ConfigureAwait(false);

            if (result is not { } completed)
            {
                Debug.WriteLine($"Clip capture host could not be started for {path}");

                return null;
            }

            if (!completed.Succeeded)
            {
                Debug.WriteLine(
                    $"Clip capture host failed for {path}: exit {completed.ExitCode} {completed.Error.Trim()}");

                if (completed.Output.Length > 0)
                    Debug.WriteLine($"Clip capture host output: {completed.Output.Trim()}");

                return null;
            }

            frames = ReadFrames(directory);

            return frames.Count > 0 ? new ClipCaptureResult(frames, framesPerSecond) : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clip capture failed for {path}: {ex}");

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<string> Arguments(
        string path,
        TimeSpan captureAt,
        TimeSpan duration,
        int framesPerSecond,
        string directory) =>
        [
            ClipCaptureProtocol.InputArgument, path,
            ClipCaptureProtocol.CaptureAtArgument, Seconds(captureAt),
            ClipCaptureProtocol.DurationArgument, Seconds(duration),
            ClipCaptureProtocol.FramesPerSecondArgument, framesPerSecond.ToString(CultureInfo.InvariantCulture),
            ClipCaptureProtocol.WidthArgument, Options.FrameWidth.ToString(CultureInfo.InvariantCulture),
            ClipCaptureProtocol.OutputArgument, directory,
        ];

    private string CacheDirectory(string path, TimeSpan captureAt, TimeSpan duration, int framesPerSecond)
    {
        var info = new FileInfo(path);

        var key = string.Join('|',
            path,
            Seconds(captureAt),
            Seconds(duration),
            framesPerSecond.ToString(CultureInfo.InvariantCulture),
            Options.FrameWidth.ToString(CultureInfo.InvariantCulture),
            info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0",
            info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        return Path.Combine(PreviewRoot, hash);
    }

    private static IReadOnlyList<string> ReadFrames(string directory)
    {
        if (!File.Exists(Path.Combine(directory, ClipCaptureProtocol.CompletionMarker)))
            return Array.Empty<string>();

        var files = Directory.GetFiles(directory, $"{ClipCaptureProtocol.FramePrefix}*.png");

        Array.Sort(files, StringComparer.Ordinal);

        return files;
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
