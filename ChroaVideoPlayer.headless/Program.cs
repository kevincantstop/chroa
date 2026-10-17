using System;

namespace ChroaVideoPlayer.Headless;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!CaptureRequest.TryParse(args, out var request, out var error))
        {
            Console.Error.WriteLine(error);

            return 2;
        }

        try
        {
            var failure = new ClipCaptureJob().Run(request);

            if (failure is null) return 0;

            Console.Error.WriteLine(failure);

            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);

            return 1;
        }
    }
}
