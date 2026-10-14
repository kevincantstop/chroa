using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer.Core.Mpv;

public sealed partial class ChroaMpvContext
{
    public async Task<byte[]?> CaptureFrameAsync()
    {
        var context = _context;
        if (context is null) return null;

        var path = Path.Combine(Path.GetTempPath(), $"chroa-frame-{Guid.NewGuid():N}.png");

        try
        {
            await context.ScreenshotToFile(path, ScreenshotOptions.Subtitles).InvokeAsync();

            for (var i = 0; i < 20 && !File.Exists(path); i++) await Task.Delay(25);

            if (!File.Exists(path))
            {
                Debug.WriteLine("screenshot-to-file produced no file");
                return null;
            }

            return await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CaptureFrame failed: {ex}");
            return null;
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not delete {path}: {ex.Message}");
        }
    }
}
