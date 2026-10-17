using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ChroaVideoPlayer.Core.ClipCapture;

public readonly record struct TaskResult(int ExitCode, string Output, string Error, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

public sealed class TaskRunner
{
    public TaskRunner(TimeSpan? timeout = null) => Timeout = timeout ?? TimeSpan.FromSeconds(30);

    public TimeSpan Timeout { get; }

    public async Task<TaskResult?> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executable))
        {
            Debug.WriteLine($"Executable not found: {executable}");

            return null;
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start);

        if (process is null) return null;

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Stop(process);

            if (cancellationToken.IsCancellationRequested) throw;

            return new TaskResult(-1, string.Empty, string.Empty, TimedOut: true);
        }

        return new TaskResult(
            process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false),
            TimedOut: false);
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not stop {process.ProcessName}: {ex.Message}");
        }
    }
}
